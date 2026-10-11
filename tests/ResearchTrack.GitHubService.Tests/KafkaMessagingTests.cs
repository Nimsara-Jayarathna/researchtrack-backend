using System.Security.Cryptography;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Webhooks;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.GitHubService.Persistence.Migrations;
using ResearchTrack.Kafka.Tests;

namespace ResearchTrack.GitHubService.Tests;

public sealed class KafkaMessagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static KafkaRuntimeOptions Options => new()
    {
        Enabled = true, Service = "github", Topic = "researchtrack.github.events.v1", ContractVersion = "1",
        ConsumerGroupId = "researchtrack-github-webhook-v1", BootstrapServers = "localhost:19092"
    };
    private static GitHubWebhookOptions WebhookOptions => new("synthetic-webhook-secret", 16384, 5,
        TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(20));

    internal static GitHubWebhookDeliveryStore Store(KafkaTestDatabase<GitHubDbContext> database, KafkaRuntimeOptions? options = null) =>
        new(database, WebhookOptions, options ?? Options);
    internal static GitHubWebhookEnvelope Envelope(string delivery = "synthetic-delivery") => new(delivery,
        "push", null, 42, 99, """{"ref":"refs/heads/main","installation":{"id":42},"repository":{"id":99,"name":"synthetic","full_name":"fixture/synthetic","owner":{"login":"fixture"},"html_url":"https://github.example.test/fixture/synthetic","default_branch":"main"},"secret":"must-not-enter-kafka"}""", "fixture-hash");

    [Fact]
    public async Task SignedIngressCreatesAtomicOutboxAndDuplicatesReuseIdentity()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        var signal = new GitHubWebhookSignal();
        var ingress = new GitHubWebhookIngressService(WebhookOptions, new GitHubWebhookSignatureVerifier(WebhookOptions),
            Store(database), signal, TimeProvider.System);
        var payload = Encoding.UTF8.GetBytes(Envelope().PayloadJson);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookOptions.Secret), payload));
        var first = await ingress.AcceptAsync(new MemoryStream(payload), payload.Length, signature, "synthetic-delivery", "push", Ct);
        var second = await ingress.AcceptAsync(new MemoryStream(payload), payload.Length, signature, "synthetic-delivery", "push", Ct);
        Assert.Equal(first.DeliveryRecordId, second.DeliveryRecordId);
        Assert.True(second.Duplicate);
        await using var db = database.CreateDbContext();
        var outbox = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        Assert.Equal(first.DeliveryRecordId, outbox.EventId);
        Assert.DoesNotContain("must-not-enter-kafka", outbox.PayloadJson);
        Assert.DoesNotContain("html_url", outbox.PayloadJson);
        var value = KafkaWebhookContract.Parse(outbox.PayloadJson, outbox.MessageKey, "github", "1");
        Assert.Equal(42, value.Data.InstallationId);
        Assert.Equal("installation:42", outbox.MessageKey);
    }

    [Fact]
    public async Task FailedOutboxInsertRollsBackAcceptedInbox()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_outbox BEFORE INSERT ON kafka_outbox_messages BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;", Ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => Store(database).AcceptAsync(Envelope(), DateTime.UtcNow, Ct));
        Assert.Empty(await db.WebhookDeliveries.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
    }

    [Theory]
    [InlineData("ping", 42)]
    [InlineData("unsupported", 42)]
    [InlineData("push", 0)]
    public async Task UnsupportedOrUnmappedEventsRemainOnExistingPath(string eventType, long installation)
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await Store(database).AcceptAsync(Envelope() with { EventType = eventType, InstallationId = installation }, DateTime.UtcNow, Ct);
        await using var db = database.CreateDbContext();
        Assert.Single(await db.WebhookDeliveries.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
    }

    [Fact]
    public async Task DisabledKafkaPreservesWebhookAcceptanceWithoutOutbox()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await Store(database, new()).AcceptAsync(Envelope(), DateTime.UtcNow, Ct);
        await using var db = database.CreateDbContext();
        Assert.Single(await db.WebhookDeliveries.ToListAsync(Ct));
        Assert.Empty(await db.Set<KafkaOutboxMessage>().ToListAsync(Ct));
        var services = new ServiceCollection().AddWebhookKafkaMessaging<GitHubDbContext, GitHubKafkaEventHandler>(new ConfigurationBuilder().Build(), "github");
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService) || x.ServiceType == typeof(IKafkaEventPublisher));
    }

    [Fact]
    public async Task ConsumerDeduplicatesDurablyAcrossHandlerRestartAndRejectsForgedIdentity()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await Store(database).AcceptAsync(Envelope(), DateTime.UtcNow, Ct);
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var value = KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "github", "1");
        await new GitHubKafkaEventHandler(database, new()).HandleAsync(value, Ct);
        await new GitHubKafkaEventHandler(database, new()).HandleAsync(value, Ct);
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        await Assert.ThrowsAsync<KafkaContractException>(() => new GitHubKafkaEventHandler(database, new()).HandleAsync(
            value with { Data = value.Data with { DeliveryId = "forged" } }, Ct));
        Assert.Equal("RECEIVED", (await db.WebhookDeliveries.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task OutboxRecoversExpiredLeaseAndPreservesOrderingWithoutOldLeaseAcknowledgement()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        var now = DateTime.UtcNow;
        await Store(database).AcceptAsync(Envelope("one"), now, Ct);
        await Store(database).AcceptAsync(Envelope("two"), now, Ct);
        var store = new KafkaOutboxStore<GitHubDbContext>(database);
        var first = Assert.IsType<KafkaOutboxMessage>(await store.ClaimAsync(now, Ct));
        Assert.Null(await store.ClaimAsync(now, Ct));
        var recovered = Assert.IsType<KafkaOutboxMessage>(await store.ClaimAsync(now.AddMinutes(3), Ct));
        Assert.Equal(first.EventId, recovered.EventId);
        Assert.NotEqual(first.LeaseId, recovered.LeaseId);
        await store.CompleteAsync(first, now.AddMinutes(3), Ct); // stale owner must not acknowledge.
        Assert.Null(await store.ClaimAsync(now.AddMinutes(3), Ct));
        await store.CompleteAsync(recovered, now.AddMinutes(3), Ct);
        var next = Assert.IsType<KafkaOutboxMessage>(await store.ClaimAsync(now.AddMinutes(3), Ct));
        Assert.NotEqual(first.EventId, next.EventId);
    }

    [Fact]
    public async Task BrokerFailureRetainsInboxAndRetriesThenHoldsAfterBoundedAttempts()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        var now = DateTime.UtcNow;
        await Store(database).AcceptAsync(Envelope(), now, Ct);
        var store = new KafkaOutboxStore<GitHubDbContext>(database);
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var row = Assert.IsType<KafkaOutboxMessage>(await store.ClaimAsync(now, Ct));
            Assert.Equal(attempt, row.AttemptCount);
            await store.FailAsync(row, now, "Local_MsgTimedOut", false, Ct);
            Assert.Null(await store.ClaimAsync(now, Ct));
            now = now.Add(KafkaOutboxStore<GitHubDbContext>.RetryDelay(attempt));
        }
        Assert.Null(await store.ClaimAsync(now, Ct));
        await using var db = database.CreateDbContext();
        Assert.Equal("FAILED", (await db.Set<KafkaOutboxMessage>().SingleAsync(Ct)).Status);
        Assert.Single(await db.WebhookDeliveries.ToListAsync(Ct));
        // Restart must not clear the health signal for a durably held outbox row.
        var health = new KafkaMessagingHealth(store);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
            (await health.CheckHealthAsync(new(), Ct)).Status);
    }

    [Fact]
    public async Task RealPublisherRequiresAcknowledgementBeforeOutboxCompletion()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await Store(database).AcceptAsync(Envelope(), DateTime.UtcNow, Ct);
        var factory = Substitute.For<IKafkaClientFactory>();
        var producer = Substitute.For<IProducer<string, string>>();
        factory.CreateProducer().Returns(producer);
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeliveryResult<string, string> { Status = PersistenceStatus.Persisted }));
        using var publisher = new KafkaEventPublisher(factory, Options);
        var worker = new KafkaOutboxWorker(new KafkaOutboxStore<GitHubDbContext>(database), publisher, new(), NullLogger<KafkaOutboxWorker>.Instance);
        Assert.True(await worker.PublishOneAsync(Ct));
        await using var db = database.CreateDbContext();
        Assert.Equal("PUBLISHED", (await db.Set<KafkaOutboxMessage>().SingleAsync(Ct)).Status);
        await producer.Received(1).ProduceAsync(Options.Topic, Arg.Is<Message<string, string>>(x => x.Key == "installation:42" && x.Headers.Count == 1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("{}")] [InlineData("not-json")]
    public void InvalidSchemaIsRejected(string payload) => Assert.Throws<KafkaContractException>(() => KafkaWebhookContract.Parse(payload, "installation:42", "github", "1"));

    [Fact]
    public async Task CommitFollowsSuccessfulHandlerAndNeverFollowsInvalidOrFailedProcessing()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await Store(database).AcceptAsync(Envelope(), DateTime.UtcNow, Ct);
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var handler = Substitute.For<IKafkaEventHandler>();
        var consumer = Substitute.For<IConsumer<string, string>>();
        var services = new ServiceCollection().AddScoped(_ => handler).BuildServiceProvider();
        using (services)
        {
            var worker = new KafkaConsumerWorker(Substitute.For<IKafkaClientFactory>(), Options, services.GetRequiredService<IServiceScopeFactory>(),
                new(Options), new(), NullLogger<KafkaConsumerWorker>.Instance);
            var record = new ConsumeResult<string, string> { Topic = row.Topic, Partition = 0, Offset = 1,
                Message = new() { Key = row.MessageKey, Value = row.PayloadJson } };
            var completed = false;
            handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>()).Returns(_ => { completed = true; return Task.CompletedTask; });
            consumer.When(x => x.Commit(Arg.Any<ConsumeResult<string, string>>())).Do(_ => Assert.True(completed));
            await worker.HandleAndCommitAsync(consumer, record, Ct);
            consumer.Received(1).Commit(record);
            consumer.ClearReceivedCalls();
            await Assert.ThrowsAsync<KafkaContractException>(() => worker.HandleAndCommitAsync(consumer, withMessage("{}"), Ct));
            consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
            handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("synthetic failure")));
            handler.ClearReceivedCalls();
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.HandleAndCommitAsync(consumer, record, Ct));
            await handler.Received(3).HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>());
            consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
            // A KafkaException raised by the business handoff is still a processing
            // failure. Only exceptions from Commit allow close/rejoin/replay.
            handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new KafkaException(new Error(ErrorCode.Local_TimedOut))));
            await Assert.ThrowsAsync<KafkaException>(() => worker.HandleAndCommitAsync(consumer, record, Ct));
            consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, string>>());
            handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            consumer.When(x => x.Commit(Arg.Any<ConsumeResult<string, string>>()))
                .Do(_ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut)));
            await Assert.ThrowsAsync<KafkaOffsetCommitException>(() => worker.HandleAndCommitAsync(consumer, record, Ct));
            ConsumeResult<string, string> withMessage(string payload) => new() { Topic = row.Topic, Message = new() { Key = row.MessageKey, Value = payload } };
        }
    }

    [Fact]
    public void ClientConfigurationPreservesTlsAndBoundedQueuesAndManualOffsets()
    {
        var producer = KafkaClientConfiguration.Producer(Options);
        var consumer = KafkaClientConfiguration.Consumer(Options);
        Assert.Equal(SecurityProtocol.Ssl, producer.SecurityProtocol);
        Assert.True(producer.EnableSslCertificateVerification);
        Assert.Equal(SslEndpointIdentificationAlgorithm.Https, producer.SslEndpointIdentificationAlgorithm);
        Assert.True(producer.EnableIdempotence);
        Assert.Equal(Acks.All, producer.Acks);
        Assert.False(producer.AllowAutoCreateTopics);
        Assert.Equal(30000, producer.MessageTimeoutMs);
        Assert.Equal(1000, producer.QueueBufferingMaxMessages);
        Assert.False(consumer.EnableAutoCommit);
        Assert.False(consumer.EnableAutoOffsetStore);
        Assert.False(consumer.AllowAutoCreateTopics);
    }

    [Fact]
    public void MigrationOnlyAddsKafkaTablesAndIndexes()
    {
        var migration = new AddKafkaWebhookMessaging();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation or Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation));
        Assert.Equal(2, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Count());
    }
}
