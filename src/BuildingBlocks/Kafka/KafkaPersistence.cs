using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.BuildingBlocks.Kafka;

public sealed class KafkaOutboxMessage
{
    public long Sequence { get; set; }
    public Guid EventId { get; set; }
    public string Topic { get; set; } = "";
    public string MessageKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }

    public static KafkaOutboxMessage Create(WebhookReadyEvent value, KafkaRuntimeOptions options)
    {
        var key = KafkaWebhookContract.Key(value);
        var payload = KafkaWebhookContract.Serialize(value);
        KafkaWebhookContract.Parse(payload, key, value.EventName.Split('.')[0], options.ContractVersion);
        return new()
        {
            EventId = value.EventId, Topic = options.Topic, MessageKey = key, PayloadJson = payload,
            CreatedAtUtc = value.OccurredAtUtc.UtcDateTime, NextAttemptAtUtc = value.OccurredAtUtc.UtcDateTime
        };
    }
}

public sealed class KafkaInboxReceipt
{
    public Guid EventId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTime HandedOffAtUtc { get; set; }
}

public static class KafkaPersistence
{
    public static async Task AssertOwnedAsync(DbContext db, WebhookReadyEvent value, CancellationToken ct)
    {
        var outbox = await db.Set<KafkaOutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == value.EventId, ct);
        if (outbox is null || outbox.PayloadJson != KafkaWebhookContract.Serialize(value) || outbox.MessageKey != KafkaWebhookContract.Key(value))
            throw new KafkaContractException();
    }
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<KafkaOutboxMessage>(entity =>
        {
            entity.ToTable("kafka_outbox_messages");
            entity.HasKey(x => x.Sequence);
            entity.Property(x => x.Sequence).ValueGeneratedOnAdd();
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.MessageKey, x.Sequence });
            entity.Property(x => x.Topic).HasMaxLength(249).IsRequired();
            entity.Property(x => x.MessageKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("text").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(64);
        });
        builder.Entity<KafkaInboxReceipt>(entity =>
        {
            entity.ToTable("kafka_inbox_receipts");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        });
    }

    public static async Task<bool> AlreadyHandledAsync(DbContext db, WebhookReadyEvent value, CancellationToken ct)
    {
        var receipt = await db.Set<KafkaInboxReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == value.EventId, ct);
        if (receipt is null) return false;
        if (receipt.PayloadHash != KafkaWebhookContract.Hash(value)) throw new KafkaContractException();
        return true;
    }

    public static void AddReceipt(DbContext db, WebhookReadyEvent value, string outcome) =>
        db.Set<KafkaInboxReceipt>().Add(new()
        {
            EventId = value.EventId, PayloadHash = KafkaWebhookContract.Hash(value),
            Outcome = outcome, HandedOffAtUtc = DateTime.UtcNow
        });
}

public interface IKafkaOutboxStore
{
    Task<KafkaOutboxMessage?> ClaimAsync(DateTime now, CancellationToken ct);
    Task CompleteAsync(KafkaOutboxMessage message, DateTime now, CancellationToken ct);
    Task FailAsync(KafkaOutboxMessage message, DateTime now, string errorCode, bool permanent, CancellationToken ct);
}

public sealed class KafkaOutboxStore<TContext>(IDbContextFactory<TContext> factory) : IKafkaOutboxStore where TContext : DbContext
{
    public async Task<KafkaOutboxMessage?> ClaimAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = db.Set<KafkaOutboxMessage>();
        await rows.Where(x => x.Status == "PUBLISHING" && x.AttemptCount >= 10 && x.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "FAILED")
                .SetProperty(x => x.LastErrorCode, "lease_expired_exhausted"), ct);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = await rows.AsNoTracking().Where(x => x.AttemptCount < 10 &&
                ((x.Status == "PENDING" && x.NextAttemptAtUtc <= now) ||
                 (x.Status == "PUBLISHING" && x.LeaseExpiresAtUtc <= now)) &&
                !rows.Any(previous => previous.MessageKey == x.MessageKey && previous.Sequence < x.Sequence && previous.Status != "PUBLISHED"))
                .OrderBy(x => x.Sequence).FirstOrDefaultAsync(ct);
            if (candidate is null) return null;
            var lease = Guid.NewGuid();
            var changed = await rows.Where(x => x.Sequence == candidate.Sequence && x.AttemptCount == candidate.AttemptCount &&
                    ((x.Status == "PENDING" && x.NextAttemptAtUtc <= now) ||
                     (x.Status == "PUBLISHING" && x.LeaseExpiresAtUtc <= now)))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "PUBLISHING")
                    .SetProperty(x => x.LeaseId, lease).SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(2))
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
            if (changed == 1) return await rows.AsNoTracking().SingleAsync(x => x.Sequence == candidate.Sequence, ct);
        }
        return null;
    }

    public async Task CompleteAsync(KafkaOutboxMessage message, DateTime now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Set<KafkaOutboxMessage>().Where(x => x.Sequence == message.Sequence && x.LeaseId == message.LeaseId && x.Status == "PUBLISHING")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "PUBLISHED")
                .SetProperty(x => x.PublishedAtUtc, now).SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.LastErrorCode, (string?)null), ct);
    }

    public async Task FailAsync(KafkaOutboxMessage message, DateTime now, string errorCode, bool permanent, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var failed = permanent || message.AttemptCount >= 10;
        await db.Set<KafkaOutboxMessage>().Where(x => x.Sequence == message.Sequence && x.LeaseId == message.LeaseId && x.Status == "PUBLISHING")
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, failed ? "FAILED" : "PENDING")
                .SetProperty(x => x.NextAttemptAtUtc, now.Add(RetryDelay(message.AttemptCount)))
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null).SetProperty(x => x.LastErrorCode, errorCode), ct);
    }

    public static TimeSpan RetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(30), 2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10), 4 => TimeSpan.FromMinutes(30), _ => TimeSpan.FromHours(1)
    };
}
