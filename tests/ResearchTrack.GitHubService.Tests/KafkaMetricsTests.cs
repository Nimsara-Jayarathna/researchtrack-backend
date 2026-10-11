using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Prometheus;
using ResearchTrack.BuildingBlocks.Kafka;
using ResearchTrack.GitHubService.Features.Webhooks;
using ResearchTrack.GitHubService.Persistence;
using ResearchTrack.Kafka.Tests;

namespace ResearchTrack.GitHubService.Tests;

public sealed class KafkaMetricsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static KafkaRuntimeOptions Options => new() { Enabled = true, Service = "github", Topic = "researchtrack.github.events.v1", ContractVersion = "1" };
    private static double V(string text, string name, params (string, string)[] labels) => KafkaMetricSnapshot.Value(text, name, [("service", "github"), .. labels]);

    [Fact]
    public async Task PublicationOutcomesRetriesAndPermanentFailuresAreMonotonic()
    {
        using var db = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await KafkaMessagingTests.Store(db).AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        var publisher = Substitute.For<IKafkaEventPublisher>();
        publisher.PublishAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException("private synthetic detail")));
        var store = new KafkaOutboxStore<GitHubDbContext>(db);
        using var worker = new KafkaOutboxWorker(store, publisher, new(), NullLogger<KafkaOutboxWorker>.Instance, metrics);
        Assert.True(await worker.PublishOneAsync(Ct));
        var first = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(first, "researchtrack_kafka_publish_total", ("outcome", "retryable_failure")));
        Assert.Equal(0, V(first, "researchtrack_kafka_publish_total", ("outcome", "success")));
        Assert.Equal(0, V(first, "researchtrack_kafka_retry_total", ("stage", "publish")));
        await using (var context = db.CreateDbContext())
            await context.Set<KafkaOutboxMessage>().ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)), Ct);
        publisher.PublishAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new KafkaContractException()));
        Assert.True(await worker.PublishOneAsync(Ct));
        var after = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(after, "researchtrack_kafka_publish_total", ("outcome", "retryable_failure")));
        Assert.Equal(1, V(after, "researchtrack_kafka_publish_total", ("outcome", "permanent_failure")));
        Assert.Equal(1, V(after, "researchtrack_kafka_retry_total", ("stage", "publish")));
        Assert.Equal(2, V(after, "researchtrack_kafka_publish_duration_seconds_count"));
        var sample = await store.SampleAsync(Ct);
        Assert.Equal(new KafkaOutboxSnapshot(0, 1), sample);
        Assert.DoesNotContain("private synthetic detail", after);
        KafkaMetricSnapshot.AssertBoundedLabels(after, "github");
    }

    [Fact]
    public async Task FailedPublicationRemainsObservableWhenFailureBookkeepingAlsoFails()
    {
        var store = Substitute.For<IKafkaOutboxStore>();
        var publisher = Substitute.For<IKafkaEventPublisher>();
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        store.ClaimAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new KafkaOutboxMessage { AttemptCount = 1 });
        publisher.PublishAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException("synthetic transport error")));
        store.FailAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("synthetic database error")));
        using var worker = new KafkaOutboxWorker(store, publisher, new(), NullLogger<KafkaOutboxWorker>.Instance, metrics);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.PublishOneAsync(Ct));
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(text, "researchtrack_kafka_publish_total", ("outcome", "retryable_failure")));
        Assert.Equal(0, V(text, "researchtrack_kafka_publish_total", ("outcome", "success")));
        Assert.Equal(1, V(text, "researchtrack_kafka_publish_duration_seconds_count"));
        Assert.DoesNotContain("synthetic", text);
    }

    [Fact]
    public async Task AcknowledgedPublicationCountsSuccessOnlyAfterBookkeeping()
    {
        var store = Substitute.For<IKafkaOutboxStore>();
        var publisher = Substitute.For<IKafkaEventPublisher>();
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        store.ClaimAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new KafkaOutboxMessage { AttemptCount = 1 });
        publisher.PublishAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.CompleteAsync(Arg.Any<KafkaOutboxMessage>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Assert.Equal(0, V(await KafkaMetricSnapshot.ExportAsync(registry, Ct), "researchtrack_kafka_publish_total", ("outcome", "success")));
        });
        using var worker = new KafkaOutboxWorker(store, publisher, new(), NullLogger<KafkaOutboxWorker>.Instance, metrics);
        Assert.True(await worker.PublishOneAsync(Ct));
        Assert.Equal(1, V(await KafkaMetricSnapshot.ExportAsync(registry, Ct), "researchtrack_kafka_publish_total", ("outcome", "success")));
    }

    [Fact]
    public async Task ActualHandoffDeduplicatesSuccessAndCommitFailuresAreSeparate()
    {
        using var db = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await KafkaMessagingTests.Store(db).AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        await using var context = db.CreateDbContext();
        var row = await context.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        using var services = new ServiceCollection().AddScoped<IKafkaEventHandler>(_ => new GitHubKafkaEventHandler(db, new(), metrics)).BuildServiceProvider();
        using var worker = new KafkaConsumerWorker(Substitute.For<IKafkaClientFactory>(), Options, services.GetRequiredService<IServiceScopeFactory>(),
            new(Options, metrics), new(null, metrics), NullLogger<KafkaConsumerWorker>.Instance, metrics);
        var consumer = Substitute.For<IConsumer<string, string>>();
        var record = new ConsumeResult<string, string> { Topic = row.Topic, Partition = 0, Offset = 0, Message = new() { Key = row.MessageKey, Value = row.PayloadJson } };
        await worker.HandleAndCommitAsync(consumer, record, Ct);
        await worker.HandleAndCommitAsync(consumer, record, Ct);
        consumer.When(x => x.Commit(record)).Do(_ => throw new KafkaException(new Error(ErrorCode.Local_TimedOut)));
        await Assert.ThrowsAsync<KafkaOffsetCommitException>(() => worker.HandleAndCommitAsync(consumer, record, Ct));
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        Assert.Equal(2, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "duplicate")));
        Assert.Equal(2, V(text, "researchtrack_kafka_consume_total", ("stage", "commit"), ("outcome", "success")));
        Assert.Equal(1, V(text, "researchtrack_kafka_consume_total", ("stage", "commit"), ("outcome", "failed")));
        Assert.Equal(0, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "failed")));
        Assert.Equal(3, V(text, "researchtrack_kafka_processing_duration_seconds_count"));
        KafkaMetricSnapshot.AssertBoundedLabels(text, "github");
    }

    [Fact]
    public async Task FailedProcessingCountsAttemptsRetriesAndNoSuccess()
    {
        using var db = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await KafkaMessagingTests.Store(db).AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        await using var context = db.CreateDbContext();
        var row = await context.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        var handler = Substitute.For<IKafkaEventHandler>();
        handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("synthetic payload must not be a label")));
        var processor = new KafkaRecordProcessor(Options, metrics);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(new() { Topic = row.Topic, Message = new() { Key = row.MessageKey, Value = row.PayloadJson } }, handler, Ct));
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(3, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "failed")));
        Assert.Equal(2, V(text, "researchtrack_kafka_retry_total", ("stage", "process")));
        Assert.Equal(0, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        Assert.DoesNotContain("synthetic payload", text);
    }

    [Fact]
    public async Task RetryThatSucceedsCountsOnlyOneDurableHandoff()
    {
        using var db = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await KafkaMessagingTests.Store(db).AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        await using var context = db.CreateDbContext();
        var row = await context.Set<KafkaOutboxMessage>().SingleAsync(Ct);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        var actual = new GitHubKafkaEventHandler(db, new(), metrics);
        var handler = Substitute.For<IKafkaEventHandler>();
        var attempts = 0;
        handler.HandleAsync(Arg.Any<WebhookReadyEvent>(), Arg.Any<CancellationToken>()).Returns(call =>
            ++attempts == 1 ? Task.FromException(new IOException("synthetic transient")) : actual.HandleAsync(call.ArgAt<WebhookReadyEvent>(0), call.ArgAt<CancellationToken>(1)));
        await new KafkaRecordProcessor(Options, metrics).ProcessAsync(new() { Topic = row.Topic, Message = new() { Key = row.MessageKey, Value = row.PayloadJson } }, handler, Ct);
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "failed")));
        Assert.Equal(1, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "success")));
        Assert.Equal(1, V(text, "researchtrack_kafka_retry_total", ("stage", "process")));
    }

    [Fact]
    public async Task InvalidContractRejectedAndHeldSignalUsesBoundedLabels()
    {
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        var health = new KafkaMessagingHealth(null, metrics);
        await Assert.ThrowsAsync<KafkaContractException>(() => new KafkaRecordProcessor(Options, metrics).ProcessAsync(
            new() { Topic = Options.Topic, Message = new() { Key = "sensitive-repository", Value = "{}" } }, Substitute.For<IKafkaEventHandler>(), Ct));
        health.HoldConsumer();
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(1, V(text, "researchtrack_kafka_consumer_held"));
        Assert.Equal(1, V(text, "researchtrack_kafka_consume_total", ("stage", "process"), ("outcome", "rejected")));
        Assert.DoesNotContain("sensitive-repository", text);
        KafkaMetricSnapshot.AssertBoundedLabels(text, "github");
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, (await health.CheckHealthAsync(new(), Ct)).Status);
    }

    [Fact]
    public async Task SamplingIsCachedAndFailedSampleIsExplicitlyUnknown()
    {
        var registry = Metrics.NewCustomRegistry();
        var metrics = new KafkaMetrics("github", true, registry);
        var store = Substitute.For<IKafkaOutboxStore>();
        store.SampleAsync(Arg.Any<CancellationToken>()).Returns(new KafkaOutboxSnapshot(2, 1));
        using var worker = new KafkaOutboxMetricsWorker(store, metrics, NullLogger<KafkaOutboxMetricsWorker>.Instance);
        await worker.SampleAsync(Ct);
        var text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        await KafkaMetricSnapshot.ExportAsync(registry, Ct); // Scrapes cannot query the database.
        await store.Received(1).SampleAsync(Arg.Any<CancellationToken>());
        Assert.Equal(2, V(text, "researchtrack_kafka_outbox_messages", ("status", "pending")));
        Assert.Equal(1, V(text, "researchtrack_kafka_outbox_messages", ("status", "failed")));
        Assert.Equal(1, V(text, "researchtrack_kafka_outbox_sample_success"));
        store.SampleAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<KafkaOutboxSnapshot>(new IOException("private")));
        await worker.SampleAsync(Ct);
        text = await KafkaMetricSnapshot.ExportAsync(registry, Ct);
        Assert.Equal(0, V(text, "researchtrack_kafka_outbox_sample_success"));
        Assert.Equal(2, V(text, "researchtrack_kafka_outbox_messages", ("status", "pending")));
        Assert.DoesNotContain("private", text);
    }

    [Fact]
    public async Task DisabledKafkaRegistersNoMessagingWorkersAndPreservesIngress()
    {
        var services = new ServiceCollection().AddWebhookKafkaMessaging<GitHubDbContext, GitHubKafkaEventHandler>(new ConfigurationBuilder().Build(), "github");
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IHostedService) || x.ServiceType == typeof(IKafkaClientFactory));
        using var provider = services.BuildServiceProvider();
        Assert.Equal("github", provider.GetRequiredService<KafkaMetrics>().Service);
        using var db = new KafkaTestDatabase<GitHubDbContext>(options => new(options));
        await KafkaMessagingTests.Store(db, new()).AcceptAsync(KafkaMessagingTests.Envelope(), DateTime.UtcNow, Ct);
        await using var context = db.CreateDbContext();
        Assert.Single(await context.WebhookDeliveries.ToListAsync(Ct));
        Assert.Empty(await context.Set<KafkaOutboxMessage>().ToListAsync(Ct));
        var registry = Metrics.NewCustomRegistry();
        _ = new KafkaMetrics("github", false, registry);
        Assert.Equal(0, V(await KafkaMetricSnapshot.ExportAsync(registry, Ct), "researchtrack_kafka_enabled"));
    }
}
