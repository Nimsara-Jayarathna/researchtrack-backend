using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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
    private static JiraKafkaEventHandler Handler(KafkaTestDatabase<JiraDbContext> database)
    {
        var lease = Substitute.For<IJiraKafkaScheduleLease>();
        lease.AcquireAsync(Arg.Any<JiraDbContext>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable>(new KafkaTestLease()));
        return new(database, lease);
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
        await Handler(database).HandleAsync(value, Ct);
        var job = await db.JiraSyncJobs.SingleAsync(Ct);
        Assert.Equal(project, job.ResearchProjectId);
        Assert.Equal("PENDING", job.Status);
        Assert.Equal("WEBHOOK", job.Reason);
        Assert.Equal("durable_sync_job", (await db.Set<KafkaInboxReceipt>().SingleAsync(Ct)).Outcome);
        // Restart/replay must not enqueue another job or receipt.
        await Handler(database).HandleAsync(value, Ct);
        Assert.Single(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
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
