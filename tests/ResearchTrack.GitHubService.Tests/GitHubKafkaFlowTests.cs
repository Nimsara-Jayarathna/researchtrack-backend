using Confluent.Kafka;
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
    public async Task TlsBrokerAcknowledgementRealConsumerBusinessOutcomeAndClientRestart()
    {
        using var database = new KafkaTestDatabase<GitHubDbContext>(options => new(options), IsolatedKafkaFixture.DatabaseConnection("github"));
        var options = IsolatedKafkaFixture.Options("github");
        var setup = await SetupAsync(database, options);
        using var provider = setup.Provider;
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var factory = IsolatedKafkaFixture.Factory(options);
        using (var publisher = new KafkaEventPublisher(factory, options))
            Assert.True(await new KafkaOutboxWorker(new KafkaOutboxStore<GitHubDbContext>(database), publisher, new(), NullLogger<KafkaOutboxWorker>.Instance).PublishOneAsync(Ct));
        Assert.Equal("PUBLISHED", (await db.Set<KafkaOutboxMessage>().AsNoTracking().SingleAsync(Ct)).Status);
        var worker = new KafkaConsumerWorker(factory, options, provider.GetRequiredService<IServiceScopeFactory>(), new(options), new(), NullLogger<KafkaConsumerWorker>.Instance);
        TopicPartitionOffset next;
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            var record = await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct);
            await worker.HandleAndCommitAsync(consumer, record, Ct);
            next = new(record.TopicPartition, record.Offset + 1);
            consumer.Close();
        }
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
        using (var publisher = new KafkaEventPublisher(factory, options)) await publisher.PublishAsync(row, Ct);
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            await worker.HandleAndCommitAsync(consumer, await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct), Ct);
            consumer.Close();
        }
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Single(await db.SyncRuns.ToListAsync(Ct));
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
        using var publisher = new KafkaEventPublisher(IsolatedKafkaFixture.Factory(options), options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        Assert.True(await new KafkaOutboxWorker(new KafkaOutboxStore<GitHubDbContext>(database), publisher, new(), NullLogger<KafkaOutboxWorker>.Instance).PublishOneAsync(timeout.Token));
        await using var db = database.CreateDbContext();
        Assert.Equal("PENDING", (await db.Set<KafkaOutboxMessage>().SingleAsync(Ct)).Status);
        Assert.Single(await db.WebhookDeliveries.ToListAsync(Ct));
    }

    private static async Task<(ServiceProvider Provider, Guid DeliveryId)> SetupAsync(KafkaTestDatabase<GitHubDbContext> database, KafkaRuntimeOptions options)
    {
        var sourceId = Guid.NewGuid(); var repoId = Guid.NewGuid(); var linkId = Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.AccessSources.Add(new() { Id = sourceId, ProjectId = Guid.NewGuid(), InstallationId = 42, OwnerLogin = "fixture", OwnerType = "User", AccessType = "INSTALLATION_DIRECT", ConnectionStatus = "CONNECTED", Active = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.Repositories.Add(new() { Id = repoId, SourceId = sourceId, GitHubRepositoryId = 99, FullName = "fixture/synthetic", Name = "synthetic", OwnerLogin = "fixture", DefaultBranch = "main", Url = "https://github.example.test/fixture/synthetic", Available = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.ProjectRepositoryLinks.Add(new() { Id = linkId, ProjectId = Guid.NewGuid(), SourceId = sourceId, GitHubRepositoryId = repoId, GitHubRepoId = 99, AccessType = "INSTALLATION_DIRECT", FullName = "fixture/synthetic", Name = "synthetic", OwnerLogin = "fixture", DefaultBranch = "main", Url = "https://github.example.test/fixture/synthetic", Active = true, Enabled = true, SyncStatus = "PENDING", LinkedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }
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
