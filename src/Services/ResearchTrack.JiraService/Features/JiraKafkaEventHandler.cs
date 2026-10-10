using System.Data;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraKafkaScheduleLease
{
    Task<IAsyncDisposable> AcquireAsync(JiraDbContext db, Guid projectId, CancellationToken ct);
}

public sealed class JiraKafkaScheduleLease : IJiraKafkaScheduleLease
{
    public async Task<IAsyncDisposable> AcquireAsync(JiraDbContext db, Guid projectId, CancellationToken ct) =>
        await JiraDatabaseLock.TryAcquireAsync(db, JiraDatabaseLock.ProjectSchedule(projectId), 10, ct)
        ?? throw new InvalidOperationException("Jira Kafka handoff could not acquire scheduling lease.");
}

public sealed class JiraKafkaEventHandler(IDbContextFactory<JiraDbContext> factory,
    IJiraKafkaScheduleLease scheduleLease) : IKafkaEventHandler
{
    public async Task HandleAsync(WebhookReadyEvent value, CancellationToken ct)
    {
        var projectId = value.Data.ResearchProjectId ?? throw new KafkaContractException();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var lease = await scheduleLease.AcquireAsync(db, projectId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await KafkaPersistence.AlreadyHandledAsync(db, value, ct)) return;
        await KafkaPersistence.AssertOwnedAsync(db, value, ct);
        var record = await db.JiraWebhookEvents.SingleOrDefaultAsync(x => x.Id == value.EventId, ct);
        var connection = await db.JiraConnections.AsNoTracking().SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct);
        var outcome = "removed";
        if (record is not null)
        {
            if (record.ResearchProjectId != projectId || record.CloudId != value.Data.CloudId ||
                record.EventType != value.Data.EventType || record.ReceivedAt != value.OccurredAtUtc)
                throw new KafkaContractException();
            outcome = "already_processed";
            if (record.Status == "RECEIVED")
            {
                if (connection is null || connection.CloudId != record.CloudId || connection.SyncStatus == "INVALID_AUTH")
                    outcome = "inactive_connection";
                else
                {
                    // A running durable job may already cover this webhook. Do not create
                    // another sync just because Kafka arrived after the polling worker.
                    var covered = await db.JiraSyncJobs.AnyAsync(x => x.ResearchProjectId == projectId &&
                        x.Status == "RUNNING" && x.StartedAt >= record.ReceivedAt, ct);
                    if (!covered)
                        await JiraSyncScheduler.StageAsync(db, projectId, "WEBHOOK", DateTimeOffset.UtcNow, record.JiraIssueId, ct);
                    outcome = covered ? "existing_running_job" : "durable_sync_job";
                }
            }
        }
        KafkaPersistence.AddReceipt(db, value, outcome);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
