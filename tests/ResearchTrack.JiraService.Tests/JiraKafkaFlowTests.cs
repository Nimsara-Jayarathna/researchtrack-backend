using ResearchTrack.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prometheus;
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
    public async Task AuthenticatedJiraWebhookBrokerOutageRetryCommitRecoveryAndActualSnapshot()
    {
        using var database = new KafkaTestDatabase<JiraDbContext>(options => new(options), IsolatedKafkaFixture.DatabaseConnection("jira"));
        var options = IsolatedKafkaFixture.Options("jira");
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("jira", true, registry);
        await KafkaMetricSnapshot.SaveIsolatedAsync("jira", registry, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("researchtrack_kafka_enabled{service=\"jira\"}", x => x == 1, Ct);
        var projectId = await KafkaMessagingTests.SeedAsync(database, Ct);
        var jiraOptions = KafkaMessagingTests.JiraOptions;
        using var http = new HttpClient(new SnapshotHttpHandler());
        var client = new AtlassianClient(http, jiraOptions);
        var protector = new StubTokenProtector();
        var scheduler = new JiraSyncScheduler(database);
        var webhook = new JiraWebhookService(database, client, protector, jiraOptions, scheduler, NullLogger<JiraWebhookService>.Instance, options);
        Assert.True(await webhook.ReceiveAsync(KafkaMessagingTests.Bearer(), "synthetic-delivery", KafkaMessagingTests.Payload, Ct));
        var services = new ServiceCollection().AddLogging().AddSingleton(metrics).AddSingleton<IDbContextFactory<JiraDbContext>>(database)
            .AddSingleton(jiraOptions).AddSingleton(client).AddSingleton<IJiraTokenProtector>(protector)
            .AddSingleton<IJiraSyncScheduler>(scheduler).AddSingleton<IJiraWebhookService>(webhook)
            .AddSingleton<IJiraKafkaScheduleLease, JiraKafkaScheduleLease>()
            .AddScoped<IKafkaEventHandler, JiraKafkaEventHandler>().AddScoped<IJiraSyncService, JiraSyncService>();
        using var provider = services.BuildServiceProvider();
        await using var db = database.CreateDbContext();
        var row = await db.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var factory = IsolatedKafkaFixture.Factory(options);
        var outboxStore = new KafkaOutboxStore<JiraDbContext>(database);
        var messagingHealth = new KafkaMessagingHealth(outboxStore, metrics);
        await using var httpHost = new ResearchTrackWebApplicationFactory<Program>(IsolatedKafkaFixture.DatabaseConnection("jira"), services =>
        {
            // HTTP checks use the real application pipeline and fixture MySQL;
            // flow workers below own processing, avoiding a second scheduler.
            services.RemoveAll<IHostedService>();
            services.AddHealthChecks().AddCheck("fixture-kafka", messagingHealth, tags: ["ready"]);
        });
        using var httpClient = httpHost.CreateClient();
        using var publisher = new KafkaEventPublisher(factory, options);
        using var outboxWorker = new KafkaOutboxWorker(outboxStore, publisher, messagingHealth, NullLogger<KafkaOutboxWorker>.Instance, metrics);
        var consumerWorker = new KafkaConsumerWorker(factory, options, provider.GetRequiredService<IServiceScopeFactory>(), new(options, metrics), new(null, metrics), NullLogger<KafkaConsumerWorker>.Instance, metrics);
        Offset uncommitted;
        using (var consumer = factory.CreateConsumer())
        {
            await IsolatedKafkaFixture.RecoverOutboxAsync(database, options, metrics, registry, consumer, outboxWorker, httpClient, Ct);
            var record = await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct);
            uncommitted = record.Offset;
            await IsolatedKafkaFixture.FailCommitDuringOutageAsync(consumerWorker, consumer, record, Ct);
            consumer.Close();
        }
        TopicPartitionOffset next;
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            var record = await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct);
            Assert.Equal(uncommitted, record.Offset);
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
        await publisher.PublishAsync(row, Ct);
        using (var consumer = factory.CreateConsumer())
        {
            consumer.Subscribe(options.Topic);
            await consumerWorker.HandleAndCommitAsync(consumer, await IsolatedKafkaFixture.ReceiveAsync(consumer, row.EventId, Ct), Ct);
            consumer.Close();
        }
        Assert.Single(await db.Set<KafkaInboxReceipt>().ToListAsync(Ct));
        Assert.Single(await db.JiraSyncJobs.ToListAsync(Ct));
        Assert.Equal(revision, (await db.JiraConnections.AsNoTracking().SingleAsync(Ct)).SyncRevision);
        await new KafkaOutboxMetricsWorker(new KafkaOutboxStore<JiraDbContext>(database), metrics, NullLogger<KafkaOutboxMetricsWorker>.Instance).SampleAsync(Ct);
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_publish_total", ("outcome", "success")));
        Assert.Equal(1, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        Assert.Equal(2, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "duplicate")));
        Assert.Equal(1, KafkaMetricSnapshot.Value(text, "researchtrack_kafka_consume_total", ("stage", "commit"), ("outcome", "failed")));
        await IsolatedKafkaFixture.SaveEvidenceAsync("jira-final", registry, Ct);
        await KafkaMetricSnapshot.SaveIsolatedAsync("jira", registry, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("researchtrack_kafka_consume_total{service=\"jira\",stage=\"process\",outcome=\"success\"}", x => x == 1, Ct);
        await IsolatedKafkaFixture.WaitForMetricAsync("sum(kafka_consumergroup_lag{consumergroup=\"researchtrack-jira-webhook-v1\"})", x => x == 0, Ct);
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
