using System.Diagnostics;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GitHubWebhookSignal _signal;
    private readonly GitHubWebhookOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubWebhookDeliveryWorker> _logger;

    public GitHubWebhookDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        GitHubWebhookSignal signal,
        GitHubWebhookOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubWebhookDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Existing RECEIVED/FAILED/stale PROCESSING rows are intentionally
        // recovered on startup before waiting for a new HTTP delivery.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedAny = await DrainDueAsync(stoppingToken);
                if (processedAny)
                {
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var nextDue = await store.GetNextDueAtAsync(now, stoppingToken);
                var delay = nextDue.HasValue
                    ? nextDue.Value <= now
                        ? TimeSpan.Zero
                        : nextDue.Value - now
                    : _options.PollInterval;
                if (delay <= TimeSpan.Zero)
                {
                    continue;
                }

                // Signals are process-local. In a multi-instance deployment a
                // webhook can be persisted by another instance, so never wait
                // indefinitely on the local signal. Poll the durable inbox at a
                // bounded interval and wake earlier when this process receives a
                // delivery.
                if (delay > _options.PollInterval)
                {
                    delay = _options.PollInterval;
                }
                await WaitForSignalOrDelayAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "GitHub webhook delivery worker loop failed; retrying shortly.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }


    private async Task WaitForSignalOrDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using var wakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _signal.WaitAsync(wakeCancellation.Token);
        var delayTask = Task.Delay(delay, wakeCancellation.Token);
        var completed = await Task.WhenAny(signalTask, delayTask);
        await wakeCancellation.CancelAsync();
        try
        {
            await completed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The losing waiter was cancelled intentionally.
        }
    }

    private async Task<bool> DrainDueAsync(CancellationToken cancellationToken)
    {
        var processedAny = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            GitHubWebhookDelivery? delivery;
            using (var scope = _scopeFactory.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
                delivery = await store.TryClaimNextDueAsync(
                    _timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
            }

            if (delivery is null)
            {
                return processedAny;
            }
            processedAny = true;
            await ProcessOneAsync(delivery, cancellationToken);
        }
        return processedAny;
    }

    private async Task ProcessOneAsync(
        GitHubWebhookDelivery delivery,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetUtcNow().UtcDateTime;
        var stopwatch = Stopwatch.StartNew();
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RenewLeaseLoopAsync(delivery.Id, heartbeatCancellation.Token);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IGitHubWebhookEventProcessor>();
            var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
            var synchronization = scope.ServiceProvider.GetRequiredService<IGitHubRepositorySynchronizationService>();

            var plan = await processor.ProcessAsync(delivery, cancellationToken);
            if (!plan.Ignored)
            {
                foreach (var linkedRepositoryId in plan.LinkedRepositoryIds.Distinct())
                {
                    var outcome = await synchronization.SynchronizeAsync(
                        linkedRepositoryId,
                        GitHubSyncTriggers.Webhook,
                        cancellationToken);
                    if (outcome == GitHubSynchronizationOutcome.AlreadyRunning)
                    {
                        throw new InvalidOperationException(
                            "A linked repository is already synchronizing; this webhook will be retried so the newer GitHub state is not lost.");
                    }
                }
            }

            var completedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await store.MarkProcessedAsync(delivery.Id, plan.Ignored, completedAt, cancellationToken);
            GitHubWebhookMetrics.Processed
                .WithLabels(delivery.EventType, plan.Ignored ? "ignored" : "processed")
                .Inc();
            _logger.LogInformation(
                "GitHub webhook delivery processed. DeliveryId={DeliveryId} Event={EventType} Action={Action} Ignored={Ignored} SyncTargets={SyncTargetCount} DurationMs={DurationMs}",
                delivery.DeliveryId,
                delivery.EventType,
                delivery.Action,
                plan.Ignored,
                plan.LinkedRepositoryIds.Count,
                (completedAt - started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GitHubWebhookPermanentException exception)
        {
            await MarkFailureAsync(delivery, exception, permanent: true, cancellationToken);
        }
        catch (Exception exception)
        {
            await MarkFailureAsync(delivery, exception, permanent: false, cancellationToken);
        }
        finally
        {
            try
            {
                await heartbeatCancellation.CancelAsync();
                try
                {
                    await heartbeatTask;
                }
                catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
                {
                    // Normal shutdown of the processing-lease heartbeat.
                }
            }
            finally
            {
                stopwatch.Stop();
                GitHubWebhookMetrics.ProcessingDuration
                    .WithLabels(delivery.EventType)
                    .Observe(stopwatch.Elapsed.TotalSeconds);
            }
        }
    }

    private async Task RenewLeaseLoopAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var intervalTicks = Math.Max(TimeSpan.FromSeconds(10).Ticks, _options.ProcessingLease.Ticks / 3);
        var interval = TimeSpan.FromTicks(intervalTicks);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
                await store.RenewProcessingLeaseAsync(
                    deliveryId,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A transient heartbeat failure should not abandon an otherwise
                // healthy webhook operation. The next heartbeat retries; if the
                // process dies entirely the durable lease recovery can reclaim it.
                _logger.LogWarning(
                    exception,
                    "Unable to renew GitHub webhook processing lease. DeliveryRecordId={DeliveryRecordId}",
                    deliveryId);
            }
        }
    }

    private async Task MarkFailureAsync(
        GitHubWebhookDelivery delivery,
        Exception exception,
        bool permanent,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var exhausted = delivery.AttemptCount >= _options.MaxAttempts;
        var nextAttempt = permanent || exhausted
            ? (DateTime?)null
            : now + RetryDelay(delivery.AttemptCount);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
        await store.MarkFailedAsync(
            delivery.Id,
            exception.Message,
            now,
            nextAttempt,
            cancellationToken);
        if (nextAttempt.HasValue)
        {
            _signal.Pulse();
        }

        GitHubWebhookMetrics.Processed
            .WithLabels(delivery.EventType, nextAttempt.HasValue ? "retry" : "failed")
            .Inc();
        _logger.LogError(
            exception,
            "GitHub webhook delivery failed. DeliveryId={DeliveryId} Event={EventType} Action={Action} Attempt={Attempt}/{MaxAttempts} Permanent={Permanent} NextAttemptAt={NextAttemptAt}",
            delivery.DeliveryId,
            delivery.EventType,
            delivery.Action,
            delivery.AttemptCount,
            _options.MaxAttempts,
            permanent,
            nextAttempt);
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1)
    };
}
