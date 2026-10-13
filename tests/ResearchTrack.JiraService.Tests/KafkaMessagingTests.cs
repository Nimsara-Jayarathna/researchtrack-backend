using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Prometheus;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;
using ResearchTrack.JiraService.Persistence.Migrations;
using ResearchTrack.JiraService.Tests.TestSupport;
using ResearchTrack.Kafka.Tests;

namespace ResearchTrack.JiraService.Tests;

public sealed class KafkaMessagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static KafkaRuntimeOptions Options => new()
    {
        Enabled = true, Service = "jira", Topic = "researchtrack.jira.events.v1", ContractVersion = "1",
        ConsumerGroupId = "researchtrack-jira-webhook-v1", BootstrapServers = "localhost:19092"
    };
    internal static JiraOptions JiraOptions => new()
    {
        ClientSecret = "synthetic-jira-secret", WebhookCoalesceSeconds = 1, SyncWorkerPollSeconds = 1,
        TokenUrl = "https://jira.example.test/token", ApiBaseUrl = "https://jira.example.test",
        AccessibleResourcesUrl = "https://jira.example.test/resources", ReconciliationIntervalMinutes = 15
    };
    internal static string Payload => """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9001],"issue":{"id":"123","key":"RT-42","fields":{"project":{"id":"10000","key":"RT"}}},"authorization":"must-not-enter-kafka"}""";
    internal static string Bearer()
    {
        static string Encode(string text) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(text));
        var input = Encode("""{"alg":"HS256","typ":"JWT"}""") + "." +
            Encode(JsonSerializer.Serialize(new { exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds() }));
        return "Bearer " + input + "." + WebEncoders.Base64UrlEncode(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(JiraOptions.ClientSecret), Encoding.ASCII.GetBytes(input)));
    }
    internal static JiraWebhookService Service(KafkaTestDatabase<JiraDbContext> database, KafkaRuntimeOptions? options = null) =>
        new(database, new(new HttpClient(new NoHttp()), JiraOptions), new StubTokenProtector(), JiraOptions,
            new RecordingSyncScheduler(), NullLogger<JiraWebhookService>.Instance, options ?? Options);
    internal static async Task<Guid> SeedAsync(KafkaTestDatabase<JiraDbContext> database, CancellationToken ct)
    {
        var projectId = Guid.NewGuid();
        await using var db = database.CreateDbContext();
        db.JiraConnections.Add(new()
        {
            Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "fixture-cloud", JiraProjectKey = "RT",
            JiraProjectId = "10000", WebhookId = 9001, SyncStatus = "SYNCED", AccessTokenProtected = "protected:synthetic-token",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), LastReconciledAt = DateTimeOffset.UtcNow,
            WebhookExpiresAt = DateTimeOffset.UtcNow.AddDays(20), WebhookStatus = "ACTIVE"
            , ConnectedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return projectId;
    }
    private static JiraKafkaEventHandler Handler(KafkaTestDatabase<JiraDbContext> database, KafkaMetrics? metrics = null)
    {
        var lease = Substitute.For<IJiraKafkaScheduleLease>();
        lease.AcquireAsync(Arg.Any<JiraDbContext>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable>(new KafkaTestLease()));
        return new(database, lease, metrics);
    }

    [Fact]
    public async Task AuthenticatedIngressOutboxConsumerAndPersistentJobUseApprovedIdentity()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        var project = await SeedAsync(database, Ct);
        Assert.True(await Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct));
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        Assert.DoesNotContain("must-not-enter-kafka", row.PayloadJson);
        Assert.DoesNotContain("synthetic-token", row.PayloadJson);
        Assert.Equal(project.ToString("D"), row.MessageKey);
        var value = KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1");
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("jira", true, registry);
        await Handler(database, metrics).HandleAsync(value, Ct);
        var job = await db.JiraSyncJobs.SingleAsync(Ct);
        Assert.Equal(project, job.ResearchProjectId);
        Assert.Equal("PENDING", job.Status);
        Assert.Equal("WEBHOOK", job.Reason);
        Assert.Equal("durable_sync_job", (await db.Set<KafkaInboxReceipt>().SingleAsync(Ct)).Outcome);
        // Restart/replay must not enqueue another job or receipt.
        await Handler(database, metrics).HandleAsync(value, Ct);
        var metricText = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, KafkaMetricSnapshot.Value(metricText, "researchtrack_kafka_consume_total", ("service", "jira"), ("stage", "process"), ("outcome", "success")));
        Assert.Equal(1, KafkaMetricSnapshot.Value(metricText, "researchtrack_kafka_consume_total", ("service", "jira"), ("stage", "process"), ("outcome", "duplicate")));
        KafkaMetricSnapshot.AssertBoundedLabels(metricText, "jira");
        Assert.Single(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
    }

    [Fact]
    public async Task ExactPersistedTimestampMustMatchEvenWithinTheSameSecond()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        Assert.True(await Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct));
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var value = KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1");
        var webhook = await db.JiraWebhookEvents.SingleAsync(Ct);
        webhook.ReceivedAt = value.OccurredAtUtc.AddTicks(value.OccurredAtUtc.Microsecond == 999 ? -10 : 10);
        await db.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<KafkaContractException>(() => Handler(database).HandleAsync(value, Ct));
        Assert.Empty(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Empty(await db.JiraSyncJobs.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(-10, false)]
    [InlineData(0, true)]
    [InlineData(10, true)]
    public async Task RunningJobCoverageUsesExactPersistedTimestampWhenMaterializationLosesMicroseconds(int startedAtTicks, bool covered)
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(
            new DbContextOptionsBuilder<JiraDbContext>(options)
                .AddInterceptors(new WebhookTimestampMaterialization()).Options));
        var project = await SeedAsync(database, Ct);
        var receivedAt = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
        var id = Guid.NewGuid();
        var value = new WebhookReadyEvent(id, "jira.webhook.ready", "1", receivedAt, id,
            new() { WebhookRecordId = id, ResearchProjectId = project, CloudId = "fixture-cloud", EventType = "jira:issue_updated" });
        await using var db = database.CreateDbContext();
        db.JiraWebhookEvents.Add(new()
        {
            Id = id, DeliveryId = "precision-delivery", CloudId = "fixture-cloud", ResearchProjectId = project,
            EventType = "jira:issue_updated", PayloadJson = Payload, ReceivedAt = receivedAt, Status = "RECEIVED"
        });
        db.Set<KafkaOutboxMessage>().Add(KafkaOutboxMessage.Create(value, Options));
        db.JiraSyncJobs.Add(new()
        {
            Id = Guid.NewGuid(), ResearchProjectId = project, Reason = "WEBHOOK", Status = "RUNNING",
            StartedAt = receivedAt.AddTicks(startedAtTicks), RequestedAt = receivedAt.AddMinutes(-1), AvailableAt = receivedAt.AddMinutes(-1)
        });
        await db.SaveChangesAsync(Ct);

        await Handler(database).HandleAsync(value, Ct);

        Assert.Equal(covered ? "existing_running_job" : "durable_sync_job", (await db.Set<KafkaInboxReceipt>().SingleAsync(Ct)).Outcome);
        Assert.Equal(covered ? 0 : 1, await db.JiraSyncJobs.CountAsync(x => x.Status == "PENDING", Ct));
        Assert.Single(await db.JiraSyncJobs.Where(x => x.Status == "RUNNING").ToListAsync(Ct));
    }

    // Model the existing Connector/NET read limitation without changing stored timestamps.
    // Contract validation and scheduling must still use the exact persisted datetime(6).
    private sealed class WebhookTimestampMaterialization : IMaterializationInterceptor
    {
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is JiraWebhookEvent webhook)
                webhook.ReceivedAt = new DateTimeOffset(
                    webhook.ReceivedAt.UtcTicks - webhook.ReceivedAt.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            return entity;
        }
    }

    [Fact]
    public async Task DuplicateWebhookDoesNotCreateAnotherOutboxEvent()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        var service = Service(database);
        Assert.True(await service.ReceiveAsync(Bearer(), "same-delivery", Payload, Ct));
        Assert.True(await service.ReceiveAsync(Bearer(), "same-delivery", Payload, Ct));
        await using var db = database.CreateDbContext();
        Assert.Single(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
        Assert.Single(await db.JiraWebhookEvents.ToListAsync(Ct));
    }

    [Fact]
    public async Task SameDeliveryIdentifierInDifferentCloudsRetainsBothEventsAndDeduplicatesEach()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        var firstProject = await SeedAsync(database, Ct);
        var secondProject = await SeedAsync(database, Ct);
        await using var db = database.CreateDbContext();
        var second = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == secondProject, Ct);
        second.CloudId = "second-cloud";
        second.WebhookId = 9002;
        second.JiraProjectKey = "BT";
        second.JiraProjectId = "20000";
        await db.SaveChangesAsync(Ct);
        var service = Service(database);
        const string secondPayload = """{"webhookEvent":"jira:issue_updated","matchedWebhookIds":[9002],"issue":{"id":"456","key":"BT-1","fields":{"project":{"id":"20000","key":"BT"}}}}""";

        Assert.True(await service.ReceiveAsync(Bearer(), "tenant-local-delivery", Payload, Ct));
        Assert.True(await service.ReceiveAsync(Bearer(), "tenant-local-delivery", secondPayload, Ct));
        Assert.True(await service.ReceiveAsync(Bearer(), "tenant-local-delivery", Payload, Ct));
        Assert.True(await service.ReceiveAsync(Bearer(), "tenant-local-delivery", secondPayload, Ct));

        var events = await db.JiraWebhookEvents.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, x => x.ResearchProjectId == firstProject && x.CloudId == "fixture-cloud");
        Assert.Contains(events, x => x.ResearchProjectId == secondProject && x.CloudId == "second-cloud");
        var outbox = await db.Set<KafkaOutboxMessage>().AsNoTracking().ToListAsync(Ct);
        Assert.Equal(2, outbox.Count);
        foreach (var row in outbox)
            await Handler(database).HandleAsync(KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1"), Ct);
        Assert.Equal(2, await db.Set<KafkaInboxReceipt>().CountAsync(Ct));
        Assert.Equal(2, await db.JiraSyncJobs.CountAsync(Ct));
    }

    [Fact]
    public async Task OutboxFailureRollsBackJiraWebhookAcceptance()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_outbox BEFORE INSERT ON kafka_outbox_messages BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;", Ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct));
        Assert.Empty(await db.JiraWebhookEvents.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
    }

    [Fact]
    public async Task FailedReceiptInsertRollsBackDurableJobHandoff()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        await Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct);
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_receipt BEFORE INSERT ON kafka_inbox_receipts BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;", Ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => Handler(database).HandleAsync(
            KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1"), Ct));
        Assert.Empty(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
    }

    [Theory]
    [InlineData("PROCESSED", "SYNCED", "already_processed")]
    [InlineData("RECEIVED", "INVALID_AUTH", "inactive_connection")]
    public async Task CompletedOrRevokedConnectionDoesNotRepeatBusinessWork(string status, string auth, string expected)
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        await Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct);
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        (await db.JiraWebhookEvents.SingleAsync(Ct)).Status = status;
        (await db.JiraConnections.SingleAsync(Ct)).SyncStatus = auth;
        await db.SaveChangesAsync(Ct);
        await Handler(database).HandleAsync(KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1"), Ct);
        Assert.Empty(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Equal(expected, (await db.Set<KafkaInboxReceipt>().SingleAsync(Ct)).Outcome);
    }

    [Fact]
    public async Task RunningJobThatCoversWebhookDoesNotGetAnotherFollowUp()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        var project = await SeedAsync(database, Ct);
        await Service(database).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct);
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        db.JiraSyncJobs.Add(new() { Id = Guid.NewGuid(), ResearchProjectId = project, Reason = "WEBHOOK", Status = "RUNNING", StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        await Handler(database).HandleAsync(KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "jira", "1"), Ct);
        Assert.Single(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Equal("existing_running_job", (await db.Set<KafkaInboxReceipt>().SingleAsync(Ct)).Outcome);
    }

    [Fact]
    public async Task DisabledKafkaRetainsExistingJiraWebhookBehavior()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options));
        await SeedAsync(database, Ct);
        Assert.True(await Service(database, new()).ReceiveAsync(Bearer(), "synthetic-delivery", Payload, Ct));
        await using var db = database.CreateDbContext();
        Assert.Single(await db.JiraWebhookEvents.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
    }

    [Fact]
    public void StrictSchemaRejectsUnknownVersionDuplicateFieldsAndExtraSecrets()
    {
        var id = Guid.NewGuid();
        var value = new WebhookReadyEvent(id, "jira.webhook.ready", "1", DateTimeOffset.UtcNow, id,
            new() { WebhookRecordId = id, ResearchProjectId = Guid.NewGuid(), CloudId = "fixture-cloud", EventType = "jira:issue_updated" });
        var payload = KafkaWebhookContract.Serialize(value);
        Assert.Throws<KafkaContractException>(() => KafkaWebhookContract.Parse(payload.Replace("\"contractVersion\":\"1\"", "\"contractVersion\":\"2\"", StringComparison.Ordinal), KafkaWebhookContract.Key(value), "jira", "1"));
        Assert.Throws<KafkaContractException>(() => KafkaWebhookContract.Parse(payload.Replace("\"contractVersion\":\"1\"", "\"contractVersion\":\"1\",\"contractVersion\":\"1\"", StringComparison.Ordinal), KafkaWebhookContract.Key(value), "jira", "1"));
        Assert.Throws<KafkaContractException>(() => KafkaWebhookContract.Parse(payload.Replace("\"cloudId\":", "\"secret\":\"sensitive\",\"cloudId\":", StringComparison.Ordinal), KafkaWebhookContract.Key(value), "jira", "1"));
        Assert.Throws<KafkaContractException>(() => KafkaWebhookContract.Parse(payload, Guid.NewGuid().ToString("D"), "jira", "1"));
    }

    [Fact]
    public void MigrationDoesNotChangeExistingJiraColumns()
    {
        var migration = new AddKafkaWebhookMessaging();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation or Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation));
        Assert.Equal(2, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Count());
    }

    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Webhook acceptance must not call a remote API.");
    }
}
