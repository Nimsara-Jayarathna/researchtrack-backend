using Microsoft.EntityFrameworkCore;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public sealed class JiraSyncWorker : BackgroundService
{
    private static readonly TimeSpan StaleRunningJobAge = TimeSpan.FromMinutes(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly JiraOptions _options;
    private readonly ILogger<JiraSyncWorker> _logger;

    public JiraSyncWorker(IServiceScopeFactory scopes, JiraOptions options, ILogger<JiraSyncWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
        JiraOperationalMetrics.ReconciliationInterval.Set(Math.Max(1, options.ReconciliationIntervalMinutes) * 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStaleJobsAsync(stoppingToken);
                await QueueReconciliationAsync(stoppingToken);
                await RefreshWebhookRegistrationsAsync(stoppingToken);
                await ProcessOneAsync(stoppingToken);
                await UpdateQueueDepthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Jira synchronization worker iteration failed."); }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.SyncWorkerPollSeconds)), stoppingToken);
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var lease = await JiraDatabaseLock.TryAcquireAsync(db, JiraDatabaseLock.JobClaim, 1, ct);
        if (lease is null) return;

        var cutoff = DateTimeOffset.UtcNow - StaleRunningJobAge;
        var stale = await db.JiraSyncJobs
            .Where(x => x.Status == "RUNNING" && x.StartedAt.HasValue && x.StartedAt < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var job in stale)
        {
            job.Status = "PENDING";
            job.AvailableAt = now;
            job.StartedAt = null;
            job.LastError = "Recovered after a worker stopped before completing this synchronization.";
        }
        await db.SaveChangesAsync(ct);
        JiraOperationalMetrics.StaleJobsRecovered.Inc(stale.Count);
        _logger.LogWarning("Recovered {Count} stale Jira synchronization job(s).", stale.Count);
    }

    private async Task QueueReconciliationAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJiraSyncScheduler>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, _options.ReconciliationIntervalMinutes));
        var ids = await db.JiraConnections.AsNoTracking()
            .Where(x => !x.LastReconciledAt.HasValue || x.LastReconciledAt < cutoff)
            .OrderBy(x => x.LastReconciledAt)
            .Select(x => x.ResearchProjectId)
            .Take(20)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                await scheduler.RequestAsync(id, "RECONCILIATION", now, null, ct);
                JiraOperationalMetrics.ReconciliationProjects.WithLabels("queued").Inc();
            }
            catch
            {
                failed++;
                JiraOperationalMetrics.ReconciliationProjects.WithLabels("failed").Inc();
            }
        }
        JiraOperationalMetrics.ReconciliationCycles.WithLabels(failed == 0 ? "success" : "partial_failure").Inc();
        JiraOperationalMetrics.ReconciliationLastCompleted.Set(now.ToUnixTimeSeconds());
        if (failed == 0) JiraOperationalMetrics.ReconciliationLastSuccess.Set(now.ToUnixTimeSeconds());
    }

    private async Task RefreshWebhookRegistrationsAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        var webhooks = scope.ServiceProvider.GetRequiredService<IJiraWebhookService>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(7);
        var ids = await db.JiraConnections.AsNoTracking()
            .Where(x => x.WebhookStatus != "ACTIVE" || !x.WebhookExpiresAt.HasValue || x.WebhookExpiresAt < cutoff)
            .OrderBy(x => x.UpdatedAt)
            .Select(x => x.ResearchProjectId)
            .Take(10)
            .ToListAsync(ct);
        foreach (var id in ids) await webhooks.EnsureRegisteredAsync(id, ct);
    }

    private async Task ProcessOneAsync(CancellationToken ct)
    {
        var claimed = await ClaimOneAsync(ct);
        if (claimed is null) return;
        var (jobId, projectId, startedAt, reason) = claimed.Value;

        Exception? error = null;
        try
        {
            using var scope = _scopes.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<IJiraSyncService>();
            await sync.SynchronizeAsync(projectId, ct, reason);
        }
        catch (Exception ex)
        {
            error = ex;
            _logger.LogWarning(ex, "Queued Jira synchronization failed for project {ProjectId}.", projectId);
        }

        await CompleteAsync(jobId, projectId, startedAt, reason, error, ct);
    }

    private async Task<(Guid JobId, Guid ProjectId, DateTimeOffset StartedAt, string Reason)?> ClaimOneAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var lease = await JiraDatabaseLock.TryAcquireAsync(db, JiraDatabaseLock.JobClaim, 1, ct);
        if (lease is null) return null;

        var now = DateTimeOffset.UtcNow;
        var job = await db.JiraSyncJobs
            .Where(x => x.Status == "PENDING" && x.AvailableAt <= now &&
                !db.JiraSyncJobs.Any(r => r.ResearchProjectId == x.ResearchProjectId && r.Status == "RUNNING"))
            .OrderBy(x => x.AvailableAt)
            .ThenBy(x => x.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (job is null) return null;


        job.Status = "RUNNING";
        job.StartedAt = now;
        job.CompletedAt = null;
        job.AttemptCount++;
        await db.SaveChangesAsync(ct);
        return (job.Id, job.ResearchProjectId, now, job.Reason);
    }

    private async Task CompleteAsync(Guid jobId, Guid projectId, DateTimeOffset startedAt, string reason, Exception? error, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.JiraSyncJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is null) return; // Connection may have been disconnected after the sync returned.

        var now = DateTimeOffset.UtcNow;
        if (error is null)
        {
            job.Status = "COMPLETED";
            job.CompletedAt = now;
            job.LastError = null;
            JiraOperationalMetrics.SyncJobOutcomes.WithLabels(JiraOperationalMetrics.Trigger(reason), "completed").Inc();

            // Only acknowledge webhook events that existed before this synchronization began.
            // Events received while it was running are intentionally left RECEIVED; the scheduler
            // retains a single follow-up job for them.
            var coveredEvents = await db.JiraWebhookEvents
                .Where(x => x.ResearchProjectId == projectId && x.Status == "RECEIVED" && x.ReceivedAt <= startedAt)
                .ToListAsync(ct);
            foreach (var webhookEvent in coveredEvents)
            {
                webhookEvent.Status = "PROCESSED";
                webhookEvent.ProcessedAt = now;
                webhookEvent.AttemptCount++;
                webhookEvent.LastError = null;
                JiraOperationalMetrics.WebhookEvents.WithLabels(JiraOperationalMetrics.Event(webhookEvent.EventType), "processed").Inc();
            }
        }
        else if (job.AttemptCount >= 5)
        {
            job.Status = "FAILED";
            job.CompletedAt = now;
            job.LastError = Safe(error.Message);
            JiraOperationalMetrics.SyncJobOutcomes.WithLabels(JiraOperationalMetrics.Trigger(reason), "failed").Inc();
            var coveredEvents = await db.JiraWebhookEvents
                .Where(x => x.ResearchProjectId == projectId && x.Status == "RECEIVED" && x.ReceivedAt <= startedAt)
                .ToListAsync(ct);
            foreach (var webhookEvent in coveredEvents)
            {
                webhookEvent.Status = "FAILED";
                webhookEvent.ProcessedAt = now;
                webhookEvent.AttemptCount++;
                webhookEvent.LastError = Safe(error.Message);
                JiraOperationalMetrics.WebhookEvents.WithLabels(JiraOperationalMetrics.Event(webhookEvent.EventType), "failed").Inc();
            }
        }
        else
        {
            job.Status = "PENDING";
            job.StartedAt = null;
            job.LastError = Safe(error.Message);
            job.AvailableAt = now.Add(RetryDelay(job.AttemptCount));
            JiraOperationalMetrics.SyncJobOutcomes.WithLabels(JiraOperationalMetrics.Trigger(reason), "retry_scheduled").Inc();
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateQueueDepthAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JiraDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.JiraSyncJobs.AsNoTracking()
            .Where(x => x.Status == "PENDING" || x.Status == "RUNNING" || x.Status == "FAILED")
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(ct);
        foreach (var status in new[] { "PENDING", "RUNNING", "FAILED" })
            JiraOperationalMetrics.SyncQueueDepth.WithLabels(status.ToLowerInvariant()).Set(counts.FirstOrDefault(x => x.Status == status)?.Count ?? 0);
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(15)
    };

    private static string Safe(string message) => message.Length <= 1000 ? message : message[..1000];
}
