using System.Net;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;
using ResearchTrack.JiraService.Tests.TestSupport;
using ResearchTrack.Kafka.Tests;

namespace ResearchTrack.JiraService.Tests;

public sealed class JiraKafkaFlowTests
{
    public static bool IsolatedKafkaAvailable => IsolatedKafkaFixture.Enabled;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Skip = "Requires the isolated Docker TLS Kafka/MySQL fixture.", SkipUnless = nameof(IsolatedKafkaAvailable))]
    [Trait("Category", "KafkaIntegration")]
    public async Task AuthenticatedJiraWebhookTlsAcknowledgementConsumerRestartAndActualSnapshot()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options), IsolatedKafkaFixture.DatabaseConnection("jira"));
        var options = IsolatedKafkaFixture.Options("jira");
        var projectId = await KafkaMessagingTests.SeedAsync(database, Ct);
        var jiraOptions = KafkaMessagingTests.JiraOptions;
        using var http = new HttpClient(new SnapshotHttpHandler());
        var client = new AtlassianClient(http, jiraOptions);
        var protector = new StubTokenProtector();
        var scheduler = new JiraSyncScheduler(database);
        var webhook = new JiraWebhookService(database, client, protector, jiraOptions, scheduler, NullLogger<JiraWebhookService>.Instance, options);
        Assert.True(await webhook.ReceiveAsync(KafkaMessagingTests.Bearer(), "synthetic-delivery", KafkaMessagingTests.Payload, Ct));
        var services = new ServiceCollection().AddLogging().AddSingleton<IDbContextFactory<JiraDbContext>>(database)
            .AddSingleton(jiraOptions).AddSingleton(client).AddSingleton<IJiraTokenProtector>(protector)
            .AddSingleton<IJiraSyncScheduler>(scheduler).AddSingleton<IJiraWebhookService>(webhook)
            .AddSingleton<IJiraKafkaScheduleLease, JiraKafkaScheduleLease>()
            .AddScoped<IKafkaEventHandler, JiraKafkaEventHandler>().AddScoped<IJiraSyncService, JiraSyncService>();
        using var provider = services.BuildServiceProvider();
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var factory = IsolatedKafkaFixture.Factory(options);
        using (var publisher = new KafkaEventPublisher(factory, options))
            Assert.True(await new KafkaOutboxWorker(new KafkaOutboxStore<JiraDbContext>(database), publisher, new(), NullLogger<KafkaOutboxWorker>.Instance).PublishOneAsync(Ct));
        Assert.Equal("PUBLISHED", (await db.Set<KafkaOutboxMessage>().AsNoTracking().SingleAsync(Ct)).Status);
        // Simulate consumer restart with a fetched but uncommitted record.
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct);
            consumer.Close();
        }
        var consumerWorker = new KafkaConsumerWorker(factory, options, provider.GetRequiredService<IServiceScopeFactory>(), new(options), new(), NullLogger<KafkaConsumerWorker>.Instance);
        TopicPartitionOffset next;
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            var record = await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct);
            await consumerWorker.HandleAndCommitAsync(consumer, record, Ct);
            next = new(record.TopicPartition, record.Offset + 1);
            consumer.Close();
        }
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Single(await db.JiraSyncJobs.Where(x => x.Status == "PENDING").ToListAsync(Ct));
        using (var businessWorker = new JiraSyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), jiraOptions, NullLogger<JiraSyncWorker>.Instance))
        {
            await businessWorker.StartAsync(Ct);
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    if (await db.JiraWebhookEvents.AsNoTracking().AnyAsync(x => x.Id == row.EventId && x.Status == "PROCESSED", Ct)) break;
                    await Task.Delay(100, Ct);
                }
                Assert.True(await db.JiraWebhookEvents.AsNoTracking().AnyAsync(x => x.Id == row.EventId && x.Status == "PROCESSED", Ct));
            }
            finally { await businessWorker.StopAsync(Ct); }
        }
        var issue = await db.JiraIssues.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("Synthetic Kafka issue", issue.Summary);
        Assert.Equal(projectId, issue.ResearchProjectId);
        var revision = (await db.JiraConnections.AsNoTracking().SingleAsync(Ct)).SyncRevision;
        Assert.True(revision > 0);
        using (var consumer = factory.CreateConsumer())
        {
            Assert.Equal(next.Offset, Assert.Single(consumer.Committed([next.TopicPartition], TimeSpan.FromSeconds(10))).Offset);
            consumer.Close();
        }
        using (var publisher = new KafkaEventPublisher(factory, options)) await publisher.PublishAsync(row, Ct);
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            await consumerWorker.HandleAndCommitAsync(consumer, await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct), Ct);
            consumer.Close();
        }
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Single(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Equal(revision, (await db.JiraConnections.AsNoTracking().SingleAsync(Ct)).SyncRevision);
        using var admin = new AdminClientBuilder(KafkaClientConfiguration.Producer(options)).Build();
        Assert.Equal(ErrorCode.NoError, Assert.Single(admin.GetMetadata(options.Topic, TimeSpan.FromSeconds(10)).Topics).Error.Code);
    }

    private sealed class SnapshotHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/field", StringComparison.Ordinal) ? "[]"
                : path.EndsWith("/board", StringComparison.Ordinal) ? """{"values":[],"isLast":true,"total":0}"""
                : path.EndsWith("/search/jql", StringComparison.Ordinal)
                    ? """{"issues":[{"id":"123","key":"RT-42","fields":{"summary":"Synthetic Kafka issue","issuetype":{"name":"Task"},"status":{"name":"Done","statusCategory":{"key":"done","name":"Done"}},"priority":{"name":"High"}}}],"isLast":true}"""
                    : throw new InvalidOperationException("Unexpected provider HTTP request in isolated Kafka fixture.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
