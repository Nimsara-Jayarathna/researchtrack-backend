using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ResearchTrack.BuildingBlocks.Kafka;

public interface IKafkaEventHandler
{
    Task HandleAsync(WebhookReadyEvent value, CancellationToken ct);
}

public sealed class KafkaOffsetCommitException(ErrorCode code)
    : Exception("Kafka offset commit failed after durable processing.")
{
    public ErrorCode Code { get; } = code;
}

public sealed class KafkaMessagingHealth(IKafkaOutboxStore? store = null, KafkaMetrics? metrics = null)
    : IHealthCheck
{
    private int _consumerHeld;
    private int _publisherFailed;

    public void HoldConsumer()
    {
        Interlocked.Exchange(ref _consumerHeld, 1);
        metrics?.HoldConsumer();
    }

    public void PublisherResult(bool success) => Interlocked.Exchange(ref _publisherFailed, success ? 0 : 1);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (Volatile.Read(ref _consumerHeld) != 0)
            return HealthCheckResult.Unhealthy(
                "Kafka consumer held; review uncommitted record before restart."
            );
        if (
            Volatile.Read(ref _publisherFailed) != 0
            || store is not null && await store.HasHeldMessagesAsync(cancellationToken)
        )
            return HealthCheckResult.Degraded(
                "Kafka publication pending or failed; durable outbox retains work."
            );
        return HealthCheckResult.Healthy(
            "Kafka workers have no recorded processing failures; not a live broker connectivity probe."
        );
    }
}

public sealed class KafkaOutboxWorker(
    IKafkaOutboxStore store,
    IKafkaEventPublisher publisher,
    KafkaMessagingHealth health,
    ILogger<KafkaOutboxWorker> logger,
    KafkaMetrics? metrics = null
) : BackgroundService
{
    public async Task<bool> PublishOneAsync(CancellationToken ct)
    {
        var row = await store.ClaimAsync(DateTime.UtcNow, ct);
        if (row is null)
            return false;
        var timer = Stopwatch.StartNew();
        if (row.AttemptCount > 1)
            metrics?.Retry(KafkaMetricStage.Publish);
        try
        {
            await publisher.PublishAsync(row, ct);
            await store.CompleteAsync(row, DateTime.UtcNow, ct);
            metrics?.Published();
            health.PublisherResult(true);
            logger.LogInformation("Kafka outbox acknowledged. CorrelationId={CorrelationId}", row.EventId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var code = exception is KafkaException kafka
                ? kafka.Error.Code.ToString()
                : exception.GetType().Name;
            var permanent =
                exception is KafkaContractException || exception is KafkaException { Error.IsFatal: true };
            metrics?.PublicationFailed(permanent || row.AttemptCount >= 10);
            await store.FailAsync(row, DateTime.UtcNow, code, permanent, ct);
            health.PublisherResult(false);
            logger.LogWarning(
                "Kafka outbox retained after publication failure. CorrelationId={CorrelationId} Code={Code} Attempt={Attempt}",
                row.EventId,
                code,
                row.AttemptCount
            );
        }
        finally
        {
            metrics?.ObservePublication(timer.Elapsed.TotalSeconds);
        }
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await PublishOneAsync(stoppingToken))
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                health.PublisherResult(false);
                logger.LogError(
                    "Kafka outbox iteration failed; work remains durable. ErrorType={ErrorType}",
                    exception.GetType().Name
                );
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

public sealed class KafkaRecordProcessor(KafkaRuntimeOptions options, KafkaMetrics? metrics = null)
{
    public async Task<Guid> ProcessAsync(
        ConsumeResult<string, string> record,
        IKafkaEventHandler handler,
        CancellationToken ct
    )
    {
        var timer = Stopwatch.StartNew();
        try
        {
            if (record.Topic != options.Topic)
                throw new KafkaContractException();
            var value = KafkaWebhookContract.Parse(
                record.Message?.Value,
                record.Message?.Key,
                options.Service,
                options.ContractVersion
            );
            for (var attempt = 1; ; attempt++)
            {
                if (attempt > 1)
                    metrics?.Retry(KafkaMetricStage.Process);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    await handler.HandleAsync(value, timeout.Token);
                    return value.CorrelationId;
                }
                catch (KafkaContractException)
                {
                    throw;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    metrics?.ProcessingFailed(rejected: false);
                    if (attempt >= 3)
                        throw;
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                }
            }
        }
        catch (KafkaContractException)
        {
            metrics?.ProcessingFailed(rejected: true);
            throw;
        }
        finally
        {
            metrics?.ObserveProcessing(timer.Elapsed.TotalSeconds);
        }
    }
}

public sealed class KafkaConsumerWorker(
    IKafkaClientFactory factory,
    KafkaRuntimeOptions options,
    IServiceScopeFactory scopes,
    KafkaRecordProcessor processor,
    KafkaMessagingHealth health,
    ILogger<KafkaConsumerWorker> logger,
    KafkaMetrics? metrics = null
) : BackgroundService
{
    // Exposed for deterministic offset-policy tests using the same production processing path.
    public async Task HandleAndCommitAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> record,
        CancellationToken ct
    )
    {
        using var scope = scopes.CreateScope();
        var correlationId = await processor.ProcessAsync(
            record,
            scope.ServiceProvider.GetRequiredService<IKafkaEventHandler>(),
            ct
        );
        try
        {
            consumer.Commit(record);
            metrics?.Committed();
        } // Never consume a later record after a failed commit.
        catch (KafkaException exception)
        {
            metrics?.CommitFailed();
            throw new KafkaOffsetCommitException(exception.Error.Code);
        }
        logger.LogInformation(
            "Kafka durable handoff and offset commit completed. CorrelationId={CorrelationId} Partition={Partition} Offset={Offset}",
            correlationId,
            record.Partition.Value,
            record.Offset.Value
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume is synchronous. Keep it off the host startup thread.
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            using var consumer = factory.CreateConsumer();
            try
            {
                consumer.Subscribe(options.Topic);
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, string> record;
                    try
                    {
                        record = consumer.Consume(stoppingToken);
                    }
                    catch (ConsumeException exception)
                    {
                        // Deserialization failures can advance the local position. Hold rather than
                        // committing a later offset over an unseen invalid record.
                        logger.LogError("Kafka consumption held. Code={Code}", exception.Error.Code);
                        metrics?.ProcessingFailed(rejected: true);
                        health.HoldConsumer();
                        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                        break;
                    }
                    try
                    {
                        await HandleAndCommitAsync(consumer, record, stoppingToken);
                    }
                    catch (KafkaOffsetCommitException exception)
                    {
                        // Processing is already durable. Rejoin from broker offsets and deduplicate;
                        // do not read a later record and accidentally commit over this one.
                        logger.LogWarning(
                            "Kafka offset commit failed; rejoining. Code={Code}",
                            exception.Code
                        );
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            "Kafka consumer held without offset commit. ErrorType={ErrorType} Partition={Partition} Offset={Offset}",
                            exception.GetType().Name,
                            record.Partition.Value,
                            record.Offset.Value
                        );
                        health.HoldConsumer();
                        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                try
                {
                    consumer.Close();
                }
                catch (KafkaException exception)
                {
                    logger.LogWarning("Kafka close failed. Code={Code}", exception.Error.Code);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

public static class KafkaMessagingExtensions
{
    public static IServiceCollection AddWebhookKafkaMessaging<TContext, THandler>(
        this IServiceCollection services,
        IConfiguration configuration,
        string service
    )
        where TContext : DbContext
        where THandler : class, IKafkaEventHandler
    {
        var enabled = configuration.GetValue<bool>("Kafka:Enabled");
        services.AddSingleton(new KafkaMetrics(service, enabled));
        if (!enabled)
            return services;
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<KafkaRuntimeOptions>();
            if (
                options.Service != service
                || options.ContractVersion != "1"
                || options.ConsumerGroupId != "researchtrack-" + service + "-webhook-v1"
            )
                throw new InvalidOperationException(
                    "Kafka contract version or consumer group does not match approved webhook routing."
                );
            return new KafkaRecordProcessor(options, provider.GetRequiredService<KafkaMetrics>());
        });
        services.AddSingleton<IKafkaClientFactory, KafkaClientFactory>();
        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<IKafkaOutboxStore, KafkaOutboxStore<TContext>>();
        services.AddScoped<IKafkaEventHandler, THandler>();
        services.AddSingleton<KafkaMessagingHealth>();
        services.AddHealthChecks().AddCheck<KafkaMessagingHealth>("kafka-messaging", tags: ["ready"]);
        services.AddHostedService<KafkaOutboxWorker>();
        services.AddHostedService<KafkaConsumerWorker>();
        services.AddHostedService<KafkaOutboxMetricsWorker>();
        return services;
    }
}

// Sample at most once per minute, with a five-second query budget. Scrapes read
// cached gauges only; database errors leave depths explicitly stale/unknown.
public sealed class KafkaOutboxMetricsWorker(
    IKafkaOutboxStore store,
    KafkaMetrics metrics,
    ILogger<KafkaOutboxMetricsWorker> logger
) : BackgroundService
{
    public async Task SampleAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            metrics.OutboxSample(await store.SampleAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            metrics.OutboxSampleFailed();
            logger.LogWarning(
                "Kafka outbox metrics sample unavailable. ErrorType={ErrorType}",
                exception.GetType().Name
            );
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SampleAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
