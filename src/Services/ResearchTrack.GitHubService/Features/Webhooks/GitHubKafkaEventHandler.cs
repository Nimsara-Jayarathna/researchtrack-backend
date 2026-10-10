using System.Data;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Persistence;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubKafkaEventHandler(IDbContextFactory<GitHubDbContext> factory,
    GitHubWebhookSignal signal) : IKafkaEventHandler
{
    public async Task HandleAsync(WebhookReadyEvent value, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await KafkaPersistence.AlreadyHandledAsync(db, value, ct)) return;
        await KafkaPersistence.AssertOwnedAsync(db, value, ct);
        var record = await db.WebhookDeliveries.SingleOrDefaultAsync(x => x.Id == value.EventId, ct);
        var outcome = "removed";
        if (record is not null)
        {
            if (record.DeliveryId != value.Data.DeliveryId || record.EventType != value.Data.EventType ||
                record.InstallationId != value.Data.InstallationId || record.GitHubRepositoryId != value.Data.RepositoryId ||
                record.ReceivedAt != value.OccurredAtUtc.UtcDateTime) throw new KafkaContractException();
            outcome = record.Status is GitHubWebhookDeliveryStatuses.Processed or GitHubWebhookDeliveryStatuses.Ignored
                ? "already_processed" : "durable_inbox";
            // Existing status/lease/retry rules own execution. Never reset FAILED or PROCESSING.
            // RECEIVED is already durable and due; pulsing only accelerates its existing worker.
        }
        KafkaPersistence.AddReceipt(db, value, outcome);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        signal.Pulse();
    }
}
