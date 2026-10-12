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
    public async Task<IAsyncDisposable> AcquireAsync(
        JiraDbContext db,
        Guid projectId,
        CancellationToken ct
    ) =>
        await JiraDatabaseLock.TryAcquireAsync(db, JiraDatabaseLock.ProjectSchedule(projectId), 10, ct)
        ?? throw new InvalidOperationException("Jira Kafka handoff could not acquire scheduling lease.");
}

public sealed class JiraKafkaEventHandler(
    IDbContextFactory<JiraDbContext> factory,
    IJiraKafkaScheduleLease scheduleLease,
    KafkaMetrics? metrics = null
) : IKafkaEventHandler
{
    public async Task HandleAsync(WebhookReadyEvent value, CancellationToken ct)
    {
        var projectId = value.Data.ResearchProjectId ?? throw new KafkaContractException();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var lease = await scheduleLease.AcquireAsync(db, projectId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        if (await KafkaPersistence.AlreadyHandledAsync(db, value, ct))
        {
            metrics?.Duplicate();
            return;
        }
        await KafkaPersistence.AssertOwnedAsync(db, value, ct);
        var record = await db.JiraWebhookEvents.SingleOrDefaultAsync(x => x.Id == value.EventId, ct);
        var connection = await db
            .JiraConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct);
        var outcome = "removed";
        if (record is not null)
        {
            if (
                record.ResearchProjectId != projectId
                || record.CloudId != value.Data.CloudId
                || record.EventType != value.Data.EventType
                ||
                // Connector/NET DateTimeOffset materialization loses microseconds.
                // Compare the exact persisted datetime(6) in SQL instead of accepting
                // a rounded timestamp or weakening owned-event validation.
                !await db.JiraWebhookEvents.AnyAsync(
                    x => x.Id == value.EventId && x.ReceivedAt == value.OccurredAtUtc,
                    ct
                )
            )
                throw new KafkaContractException();
            outcome = "already_processed";
            if (record.Status == "RECEIVED")
            {
                if (
                    connection is null
                    || connection.CloudId != record.CloudId
                    || connection.SyncStatus == "INVALID_AUTH"
                )
                    outcome = "inactive_connection";
                else
                {
                    // A running durable job may already cover this webhook. Do not create
                    // another sync just because Kafka arrived after the polling worker.
                    var covered = await db.JiraSyncJobs.AnyAsync(
                        x =>
                            x.ResearchProjectId == projectId
                            && x.Status == "RUNNING"
                            && db.JiraWebhookEvents.Any(webhook =>
                                webhook.Id == value.EventId && x.StartedAt >= webhook.ReceivedAt
                            ),
                        ct
                    );
                    if (!covered)
                        await JiraSyncScheduler.StageAsync(
                            db,
                            projectId,
                            "WEBHOOK",
                            DateTimeOffset.UtcNow,
                            record.JiraIssueId,
                            ct
                        );
                    outcome = covered ? "existing_running_job" : "durable_sync_job";
                }
            }
        }
        KafkaPersistence.AddReceipt(db, value, outcome);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        metrics?.HandedOff(outcome is not ("durable_sync_job" or "existing_running_job"));
    }
}
