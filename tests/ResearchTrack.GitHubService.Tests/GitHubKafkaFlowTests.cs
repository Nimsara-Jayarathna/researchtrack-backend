using ResearchTrack.Testing;
using Microsoft.Extensions.Hosting;
using Confluent.Kafka;
using Prometheus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Features.Webhooks;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.Kafka.Tests;

namespace ResearchTrack.GitHubService.Tests;

public sealed class GitHubKafkaFlowTests
{
    public static bool IsolatedKafkaAvailable => IsolatedKafkaFixture.Enabled;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task FreshConsumerMetadataRetriesUnknownPartitionWithoutChangingOffsets()
    {
        var calls = 0;
        var offset = await IsolatedKafkaFixture.ReadBrokerAsync(() =>
        {
            if (++calls == 1) throw new KafkaException(new Error(ErrorCode.Local_UnknownPartition));
            return new Offset(42);
        }, Ct);
        Assert.Equal(new Offset(42), offset);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task BrokerReadRejectsAuthorizationErrorsAndRespectsCancellation()
    {
        var calls = 0;
        await Assert.ThrowsAsync<KafkaException>(() => IsolatedKafkaFixture.ReadBrokerAsync<int>(() =>
        {
            calls++;
            throw new KafkaException(new Error(ErrorCode.TopicAuthorizationFailed));
        }, Ct));
        Assert.Equal(1, calls);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            IsolatedKafkaFixture.ReadBrokerAsync(() => 42, cancelled.Token));
    }

    [Fact]
    public async Task DurableHandoffRunsExistingWorkerAndActualRepositorySnapshotProcessing()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        var options = new KafkaRuntimeOptions { Enabled = true, Service = "github", Topic = "researchtrack.github.events.v1", ContractVersion = "1" };
        var setup = await SetupAsync(database, options);
        using var provider = setup.Provider;
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        await provider.GetRequiredService<IKafkaEventHandler>().HandleAsync(KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "github", "1"), Ct);
        await RunBusinessWorkerAsync(provider, database, row.EventId);
        Assert.Equal(Sha, (await db.ProjectRepositoryLinks.AsNoTracking().SingleAsync(Ct)).LastKnownHeadSha);
        Assert.Single(await db.Commits.AsNoTracking().ToListAsync(Ct));
        // Kafka replay after the existing worker completed must not repeat synchronization.
        await provider.GetRequiredService<IKafkaEventHandler>().HandleAsync(KafkaWebhookContract.Parse(row.PayloadJson, row.MessageKey, "github", "1"), Ct);
        Assert.Single(await db.SyncRuns.AsNoTracking().ToListAsync(Ct));
    }

    [Fact(Skip = "Requires the isolated Docker TLS Kafka/MySQL fixture.", SkipUnless = nameof(IsolatedKafkaAvailable))]
    [Trait("Category", "KafkaIntegration")]
    public async Task TlsBrokerOutageDurableRetryCommitRecoveryAndActualBusinessOutcome()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options), IsolatedKafkaFixture.DatabaseConnection("github"));
        var options = IsolatedKafkaFixture.Options("github");
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        await KafkaMetricSnapshot.SaveIsolatedAsync("github", registry, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("researchtrack_kafka_enabled{service=\"github\"}", x => x == 1, Ct);
        var setup = await SetupAsync(database, options, metrics);
        using var provider = setup.Provider;
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var factory = IsolatedKafkaFixture.Factory(options);
        var outboxStore = new KafkaOutboxStore<GitHubDbContext>(database);
        var messagingHealth = new KafkaMessagingHealth(outboxStore, metrics);
        await using var httpHost = new ResearchTrackWebApplicationFactory<Program>(IsolatedKafkaFixture.DatabaseConnection("github"), services =>
        {
            // HTTP checks use the real application pipeline and fixture MySQL;
            // flow workers below own processing, avoiding a second scheduler.
            foreach (var registration in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                x.ImplementationType?.Namespace?.StartsWith("ResearchTrack.", StringComparison.Ordinal) == true).ToArray())
                services.Remove(registration);
            services.AddHealthChecks().AddCheck("fixture-kafka", messagingHealth, tags: ["ready"]);
        });
        using var httpClient = httpHost.CreateClient();
        using var publisher = new KafkaEventPublisher(factory, options);
        using var outboxWorker = new KafkaOutboxWorker(outboxStore, publisher, messagingHealth, NullLogger<KafkaOutboxWorker>.Instance, metrics);
        var worker = new KafkaConsumerWorker(factory, options, provider.GetRequiredService<IServiceScopeFactory>(), new(options, metrics), new(null, metrics), NullLogger<KafkaConsumerWorker>.Instance, metrics);
        using (var consumer = factory.CreateConsumer())
        {
            await IsolatedKafkaFixture.RecoverOutboxAsync(database, options, metrics, registry, consumer, outboxWorker, httpClient, Ct);
            consumer.Close();
        }
        var next = await IsolatedKafkaFixture.RecoverConsumerCommitAsync(factory, options,
            provider.GetRequiredService<IServiceScopeFactory>(), metrics, registry, Ct);
        await RunBusinessWorkerAsync(provider, database, row.EventId);
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Equal(Sha, (await db.ProjectRepositoryLinks.AsNoTracking().SingleAsync(Ct)).LastKnownHeadSha);
        // New client instances, same topic/group, existing topic and broker offsets.
        using (var consumer = factory.CreateConsumer())
        {
            var committed = consumer.Committed([next.TopicPartition], TimeSpan.FromSeconds(10));
            Assert.Equal(next.Offset, Assert.Single(committed).Offset);
            consumer.Close();
        }
        await publisher.PublishAsync(row, Ct);
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            await worker.HandleAndCommitAsync(consumer, await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct), Ct);
            consumer.Close();
        }
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Single(await db.SyncRuns.ToListAsync(Ct));
        var sample = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, KafkaMetricSnapshot.Value(sample, "researchtrack_kafka_publish_total", ("outcome", "success")));
        Assert.Equal(1, KafkaMetricSnapshot.Value(sample, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        Assert.Equal(2, KafkaMetricSnapshot.Value(sample, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "duplicate")));
        await new KafkaOutboxMetricsWorker(new KafkaOutboxStore<GitHubDbContext>(database), metrics, NullLogger<KafkaOutboxMetricsWorker>.Instance).SampleAsync(Ct);
        Assert.Equal(1, KafkaMetricSnapshot.Value(sample, "researchtrack_kafka_consume_total", ("stage", "commit"), ("outcome", "failed")));
        await IsolatedKafkaFixture.SaveEvidenceAsync("github-final", registry, Ct);
        await KafkaMetricSnapshot.SaveIsolatedAsync("github", registry, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("researchtrack_kafka_consume_total{service=\"github\",stage=\"process\",outcome=\"success\"}", x => x == 1, Ct);
        await VerifyHeldLagAndRecoveryAsync(database, options, row, metrics, registry);
        await IsolatedKafkaFixture.WriteEvidenceAsync("github-business", new { headSha = Sha,
            commits = await db.Commits.CountAsync(Ct), syncRuns = await db.SyncRuns.CountAsync(Ct),
            inboxReceipts = await db.Set<KafkaInboxReceipt>().CountAsync(Ct), eventId = row.EventId }, Ct);
        using var admin = new AdminClientBuilder(KafkaClientConfiguration.Producer(options)).Build();
        Assert.Equal(ErrorCode.NoError, Assert.Single(admin.GetMetadata(options.Topic, TimeSpan.FromSeconds(10)).Topics).Error.Code);
    }

    [Fact(Skip = "Requires the isolated Docker TLS Kafka/MySQL fixture.", SkipUnless = nameof(IsolatedKafkaAvailable))]
    [Trait("Category", "KafkaIntegration")]
    public async Task RealProducerTimeoutRetainsDurableWebhookAndOutbox()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        var options = IsolatedKafkaFixture.Options("github");
        options.BootstrapServers = "localhost:1"; // Unavailable loopback fixture, never production.
        await new GitHubWebhookDeliveryStore(database, new("fixture-secret", 16384, 5, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1)), options)
            .AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        using var publisher = new KafkaEventPublisher(IsolatedKafkaFixture.Factory(options), options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        Assert.True(await new KafkaOutboxWorker(new KafkaOutboxStore<GitHubDbContext>(database), publisher, new(), NullLogger<KafkaOutboxWorker>.Instance, metrics).PublishOneAsync(timeout.Token));
        await using var db = database.CreateDbContext();
        Assert.Equal("PENDING", (await db.Set<KafkaOutboxMessage>().SingleAsync(Ct)).Status);
        Assert.Single(await db.WebhookDeliveries.ToListAsync(Ct));
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_publish_total", ("outcome", "retryable_failure")));
        Assert.Equal(0, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_publish_total", ("outcome", "success")));
    }

    private static async Task VerifyHeldLagAndRecoveryAsync(KafkaTestDatabase<GitHubDbContext> database,
        KafkaRuntimeOptions options, KafkaOutboxMessage row, KafkaMetrics metrics, CollectorRegistry registry)
    {
        var factory = IsolatedKafkaFixture.Factory(options);
        using (var publisher = new KafkaEventPublisher(factory, options))
            for (var i = 0; i < 128; i++) await publisher.PublishAsync(row, Ct);
        var brokenHandler = Substitute.For<IKafkaEventHandler>();
        brokenHandler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("synthetic failure")));
        using var brokenProvider = new ServiceCollection().AddSingleton(brokenHandler).BuildServiceProvider();
        using (var held = new KafkaConsumerWorker(factory, options, brokenProvider.GetRequiredService<IServiceScopeFactory>(),
            new(options, metrics), new(null, metrics), NullLogger<KafkaConsumerWorker>.Instance, metrics))
        {
            await held.StartAsync(Ct);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (KafkaMetricSnapshot.Value(await KafkaMetricSnapshot.ExportAsync(registry, Ct), "researchtrack_kafka_consumer_held") != 1)
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Disposable consumer did not hold.");
                    await Task.Delay(100, Ct);
                }
                await KafkaMetricSnapshot.SaveIsolatedAsync("github", registry, Ct);
                await IsolatedKafkaFixture.WaitForMetricAsync("sum(kafka_consumergroup_lag{consumergroup=\"researchtrack-github-webhook-v1\"})", x => x >= 128, Ct);
                foreach (var name in new[] { "ConsumerHeld", "ConsumerLagHigh", "EventProcessingFailures" })
                    await IsolatedKafkaFixture.WaitForMetricAsync($"sum(ALERTS{{alertname=\"ResearchTrackKafka{name}\",alertstate=\"firing\"}})", x => x == 1, Ct);
            }
            finally { await held.StopAsync(Ct); }
        }
        // Fresh metric registry models a reviewed service restart; receipt deduplication survives it.
        var recoveredRegistry = Metrics.NewCustomRegistry();
        var recovered = new KafkaMetrics("github", true, recoveredRegistry);
        using var provider = new ServiceCollection().AddSingleton<IDbContextFactory<GitHubDbContext>>(database)
            .AddSingleton(new GitHubWebhookSignal()).AddSingleton(recovered).AddScoped<IKafkaEventHandler, GitHubKafkaEventHandler>().BuildServiceProvider();
        using var worker = new KafkaConsumerWorker(factory, options, provider.GetRequiredService<IServiceScopeFactory>(),
            new(options, recovered), new(null, recovered), NullLogger<KafkaConsumerWorker>.Instance, recovered);
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            for (var i = 0; i < 128; i++)
                await worker.HandleAndCommitAsync(consumer, await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct), Ct);
            consumer.Close();
        }
        await new KafkaOutboxMetricsWorker(new KafkaOutboxStore<GitHubDbContext>(database), recovered, NullLogger<KafkaOutboxMetricsWorker>.Instance).SampleAsync(Ct);
        var text = await KafkaMetricSnapshot.ExportAsync(recoveredRegistry, Ct);
        Assert.Equal(128, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "duplicate")));
        Assert.Equal(0, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        await KafkaMetricSnapshot.SaveIsolatedAsync("github", recoveredRegistry, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("sum(kafka_consumergroup_lag{consumergroup=\"researchtrack-github-webhook-v1\"})", x => x == 0, Ct);
        foreach (var name in new[] { "ConsumerHeld", "ConsumerLagHigh" })
            await IsolatedKafkaFixture.WaitForMetricAsync($"absent(ALERTS{{alertname=\"ResearchTrackKafka{name}\",alertstate=\"firing\"}})", x => x == 1, Ct);
        await IsolatedKafkaFixture.SaveEvidenceAsync("github-held-recovered", recoveredRegistry, Ct);
        await using var db = database.CreateDbContext();
        Assert.Single(await db.SyncRuns.ToListAsync(Ct));
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
    }

    [Fact(Skip = "Requires isolated loopback MySQL.", SkipUnless = nameof(IsolatedKafkaAvailable))]
    [Trait("Category", "KafkaIntegration")]
    public Task HistoricalSchemaUpgradePreservesRepositoryRecordsAndAddsKafkaPersistence() =>
        KafkaMigrationUpgrade.VerifyAsync<GitHubDbContext>("github", "20260923043000_AddGitHubSyncRevision",
            options => new(options), SeedRepositoryAsync, Ct);

    private static async Task SeedRepositoryAsync(KafkaTestDatabase<GitHubDbContext> database)
    {
        var sourceId = Guid.NewGuid(); var repoId = Guid.NewGuid(); var linkId = Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.AccessSources.Add(new() { Id = sourceId, ProjectId = Guid.NewGuid(), InstallationId = 42, OwnerLogin = "fixture", OwnerType = "User", AccessType = "INSTALLATION_DIRECT", ConnectionStatus = "CONNECTED", Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.Repositories.Add(new() { Id = repoId, SourceId = sourceId, GitHubRepositoryId = 99, FullName = "fixture/synthetic", Name = "synthetic", OwnerLogin = "fixture", DefaultBranch = "main", Url = "https://github.example.test/fixture/synthetic", Available = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.ProjectRepositoryLinks.Add(new() { Id = linkId, ProjectId = Guid.NewGuid(), SourceId = sourceId, GitHubRepositoryId = repoId, GitHubRepoId = 99, AccessType = "INSTALLATION_DIRECT", FullName = "fixture/synthetic", Name = "synthetic", OwnerLogin = "fixture", DefaultBranch = "main", Url = "https://github.example.test/fixture/synthetic", Active = true, Enabled = true, SyncStatus = "PENDING", LinkedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
    }

    private static async Task<(ServiceProvider Provider, Guid DeliveryId)> SetupAsync(KafkaTestDatabase<GitHubDbContext> database, KafkaRuntimeOptions options, KafkaMetrics? metrics = null)
    {
        await SeedRepositoryAsync(database);
        var repository = new GitHubSyncRepository(99, "fixture", "synthetic", "fixture/synthetic", "https://github.example.test/fixture/synthetic", "main", false, false, false, null, null, null, null);
        var commit = new GitHubSyncCommit(Sha, "Synthetic Kafka integration commit", null, "fixture", "fixture", null, null, null, DateTime.UtcNow, DateTime.UtcNow, "https://github.example.test/commit", 1, 1, 0, 1);
        var client = Substitute.For<IGitHubRepositorySyncClient>();
        client.GetRepositoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(repository);
        client.GetCommitsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new[] { commit });
        client.GetCommitAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(commit);
        client.GetContributorsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<GitHubSyncContributor>());
        client.GetPullRequestsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<GitHubSyncPullRequest>());
        var app = Substitute.For<IGitHubAppClient>();
        app.GetInstallationAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new GitHubInstallationInfo(42, "fixture", "User", new Dictionary<string, string> { ["contents"] = "read", ["pull_requests"] = "read" }));
        var token = Substitute.For<IGitHubInstallationTokenProvider>();
        token.GetTokenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns("synthetic-token");
        var webhook = new GitHubWebhookOptions("fixture-secret", 16384, 5, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(20));
        var store = new GitHubWebhookDeliveryStore(database, webhook, options);
        var services = new ServiceCollection().AddLogging().AddSingleton<IDbContextFactory<GitHubDbContext>>(database)
            .AddSingleton(TimeProvider.System).AddSingleton(webhook).AddSingleton<GitHubWebhookSignal>()
            .AddSingleton<IGitHubWebhookDeliveryStore>(store).AddSingleton(client).AddSingleton(app).AddSingleton(token)
            .AddSingleton(Substitute.For<IGitHubInstallationRepositoryInventoryService>())
            .AddScoped<IGitHubWebhookEventProcessor, GitHubWebhookEventProcessor>()
            .AddScoped<IGitHubRepositorySynchronizationService, GitHubRepositorySynchronizationService>()
            .AddScoped<IKafkaEventHandler, GitHubKafkaEventHandler>();
        if (metrics is not null) services.AddSingleton(metrics);
        var provider = services.BuildServiceProvider();
        var envelope = KafkaMessagingTests.Envelope();
        var bytes = System.Text.Encoding.UTF8.GetBytes(envelope.PayloadJson);
        var signature = "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(webhook.Secret), bytes));
        var accepted = await new GitHubWebhookIngressService(webhook, new GitHubWebhookSignatureVerifier(webhook), store,
            provider.GetRequiredService<GitHubWebhookSignal>(), TimeProvider.System)
            .AcceptAsync(new MemoryStream(bytes), bytes.Length, signature, "synthetic-delivery", "push", Ct);
        return (provider, accepted.DeliveryRecordId);
    }

    private static async Task RunBusinessWorkerAsync(ServiceProvider provider, KafkaTestDatabase<GitHubDbContext> database, Guid deliveryId)
    {
        using var worker = new GitHubWebhookDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<GitHubWebhookSignal>(),
            provider.GetRequiredService<GitHubWebhookOptions>(), TimeProvider.System, NullLogger<GitHubWebhookDeliveryWorker>.Instance);
        await worker.StartAsync(Ct);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                await using var db = database.CreateDbContext();
                if (await db.WebhookDeliveries.AnyAsync(x => x.Id == deliveryId && x.Status == "PROCESSED", Ct)) return;
                await Task.Delay(20, Ct);
            }
            throw new TimeoutException("Existing GitHub business worker did not process the synthetic delivery.");
        }
        finally { await worker.StopAsync(Ct); }
    }
}
