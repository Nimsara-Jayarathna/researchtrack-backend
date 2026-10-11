using Confluent.Kafka;
using System.Text.Json;
using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Kafka;

namespace ResearchTrack.Kafka.Tests;

public static class IsolatedKafkaFixture
{
    public static bool Enabled => Environment.GetEnvironmentVariable("RT_KAFKA_TEST_ENABLE") == "1";
    public static KafkaRuntimeOptions Options(string service)
    {
        var endpoint = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_ENDPOINT");
        var ca = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_CA");
        // This executable fixture cannot be pointed at the Azure production broker.
        if (!Enabled || endpoint != "localhost:19092" || string.IsNullOrWhiteSpace(ca) || !File.Exists(ca))
            throw new InvalidOperationException("Use scripts/test-kafka-integration.sh with its isolated loopback TLS fixture.");
        return new()
        {
            Enabled = true, Service = service, BootstrapServers = endpoint, SslCaLocation = ca,
            Topic = $"researchtrack.{service}.events.v1", ContractVersion = "1",
            ConsumerGroupId = $"researchtrack-{service}-webhook-v1"
        };
    }
    public static string DatabaseConnection(string service)
    {
        var connection = Environment.GetEnvironmentVariable($"RT_KAFKA_TEST_{service.ToUpperInvariant()}_CONNECTION");
        if (!Enabled || connection is null || !connection.StartsWith("Server=127.0.0.1;Port=13307;Database=researchtrack_kafka_", StringComparison.Ordinal))
            throw new InvalidOperationException("An isolated loopback MySQL fixture is required.");
        return connection;
    }
    public static async Task<ConsumeResult<string, string>> ReceiveAsync(IConsumer<string, string> consumer, Guid id, CancellationToken ct)
    {
        await Task.Yield();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var record = consumer.Consume(TimeSpan.FromMilliseconds(250));
            if (record is null) continue;
            // Previously committed fixtures may be retained. No records are skipped/committed;
            // each integration fixture starts with its own fresh broker.
            if (record.Message.Value.Contains(id.ToString("D"), StringComparison.Ordinal)) return record;
            throw new InvalidOperationException("Unexpected record before the fixture correlation identity.");
        }
        throw new TimeoutException("Isolated Kafka event was not received.");
    }
    public static KafkaClientFactory Factory(KafkaRuntimeOptions options) => new(options, NullLogger<KafkaClientFactory>.Instance);

    public static async Task WaitForMetricAsync(string query, Func<double, bool> expected, CancellationToken ct)
    {
        if (!Enabled || Environment.GetEnvironmentVariable("RT_KAFKA_TEST_PROMETHEUS") != "http://127.0.0.1:19090")
            throw new InvalidOperationException("Only the isolated loopback Prometheus fixture is permitted.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var body = await client.GetStringAsync("http://127.0.0.1:19090/api/v1/query?query=" + Uri.EscapeDataString(query), ct);
                using var json = JsonDocument.Parse(body);
                var results = json.RootElement.GetProperty("data").GetProperty("result");
                if (results.GetArrayLength() == 1 && double.TryParse(results[0].GetProperty("value")[1].GetString(),
                    System.Globalization.CultureInfo.InvariantCulture, out var value) && expected(value)) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException("Isolated Prometheus metric/alert condition did not converge: " + query);
    }

    public static async Task<string> ControlBrokerAsync(string action, CancellationToken ct)
    {
        var work = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_WORK");
        var script = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_CONTROL");
        if (!Enabled || work is null || script is null ||
            Path.GetFullPath(script) != Path.GetFullPath(Path.Combine(work, "broker-control.sh")) ||
            action is not ("stop" or "start" or "snapshot"))
            throw new InvalidOperationException("Only the runner's disposable broker control is permitted.");
        using var process = new Process { StartInfo = new("bash") { RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(script);
        process.StartInfo.ArgumentList.Add(action);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var text = await output;
        if (process.ExitCode != 0) throw new InvalidOperationException("Disposable broker control failed: " + action + " " + await errors);
        if (action == "snapshot")
        {
            var path = Path.Combine(work, "metrics", "identity-" + DateTime.UtcNow.Ticks + ".json");
            await File.WriteAllTextAsync(path, text, ct);
        }
        return text;
    }

    // Extends the existing service flows: same real producer and consumer clients,
    // real MySQL outbox and unmodified production backoff. No clock/database repair.
    public static async Task RecoverOutboxAsync<TContext>(KafkaTestDatabase<TContext> database,
        KafkaRuntimeOptions options, KafkaMetrics metrics, Prometheus.CollectorRegistry registry,
        IConsumer<string, string> consumer, KafkaOutboxWorker publisher, HttpClient http, CancellationToken ct)
        where TContext : DbContext
    {
        consumer.Subscribe(options.Topic);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (consumer.Assignment.Count == 0 && DateTime.UtcNow < deadline)
            consumer.Consume(TimeSpan.FromMilliseconds(250));
        Require(consumer.Assignment.Count == 1, "Consumer did not join before outage.");
        var partition = consumer.Assignment.Single();
        var beforeOffset = consumer.Committed([partition], TimeSpan.FromSeconds(10)).Single().Offset;
        var work = Environment.GetEnvironmentVariable("RT_KAFKA_TEST_WORK")!;
        var identity = await File.ReadAllTextAsync(Path.Combine(work, "kafka-data", "meta.properties"), ct);
        using var admin = new AdminClientBuilder(KafkaClientConfiguration.Producer(options)).Build();
        var beforeTopics = admin.GetMetadata(TimeSpan.FromSeconds(10)).Topics
            .Where(t => t.Topic.StartsWith("researchtrack.", StringComparison.Ordinal))
            .Select(t => (t.Topic, Count: t.Partitions.Count)).OrderBy(t => t.Topic).ToArray();
        var beforeSnapshot = await ControlBrokerAsync("snapshot", ct);
        await KafkaMetricSnapshot.SaveIsolatedAsync(options.Service, registry, ct);
        await WaitForMetricAsync($"researchtrack_kafka_publish_total{{service=\"{options.Service}\",outcome=\"retryable_failure\"}}", x => x == 0, ct);
        await VerifyHttpAsync(http, "Healthy", ct);
        await ControlBrokerAsync("stop", ct);
        try
        {
            var timer = Stopwatch.StartNew();
            Require(await publisher.PublishOneAsync(ct), "Outage did not claim its durable event.");
            Require(timer.Elapsed < TimeSpan.FromSeconds(45), "Publication timeout exceeded its bound.");
            await using var db = database.CreateDbContext();
            var pending = await db.Set<KafkaOutboxMessage>().AsNoTracking().SingleAsync(ct);
            Require(pending.Status == "PENDING" && pending.AttemptCount == 1 && pending.LastErrorCode is not null,
                "Failed publication must retain the original event and one attempt.");
            Require(pending.NextAttemptAtUtc > DateTime.UtcNow.AddSeconds(20), "Production retry delay was not preserved.");
            Require(!await publisher.PublishOneAsync(ct), "Publisher retried before its scheduled delay.");
            Require(await db.Set<KafkaInboxReceipt>().CountAsync(ct) == 0, "Consumer committed handoff for an unpublished event.");
            Require(consumer.Consume(TimeSpan.FromMilliseconds(500)) is null, "Consumer unexpectedly received work during outage.");
            await new KafkaOutboxMetricsWorker(new KafkaOutboxStore<TContext>(database), metrics,
                NullLogger<KafkaOutboxMetricsWorker>.Instance).SampleAsync(ct);
            await KafkaMetricSnapshot.SaveIsolatedAsync(options.Service, registry, ct);
            await VerifyHttpAsync(http, "Degraded", ct);
            await WaitForMetricAsync($"researchtrack_kafka_outbox_messages{{service=\"{options.Service}\",status=\"pending\"}}", x => x == 1, ct);
            await WaitForMetricAsync($"researchtrack_kafka_publish_total{{service=\"{options.Service}\",outcome=\"retryable_failure\"}}", x => x == 1, ct);
            await WaitForMetricAsync("sum(ALERTS{alertname=~\"ResearchTrackKafka(Broker|Exporter)Unavailable\",alertstate=\"firing\"})", x => x >= 1, ct);
            await SaveEvidenceAsync(options.Service + "-outage", registry, ct);
        }
        finally
        {
            // Restore only this broker even on assertion failure/cancellation.
            using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await ControlBrokerAsync("start", recovery.Token);
        }
        Require(identity == await File.ReadAllTextAsync(Path.Combine(work, "kafka-data", "meta.properties"), ct), "KRaft identity changed.");
        var afterTopics = admin.GetMetadata(TimeSpan.FromSeconds(10)).Topics
            .Where(t => t.Topic.StartsWith("researchtrack.", StringComparison.Ordinal))
            .Select(t => (t.Topic, Count: t.Partitions.Count)).OrderBy(t => t.Topic).ToArray();
        Require(beforeTopics.SequenceEqual(afterTopics), "Existing topic names/partitions changed.");
        Require(consumer.Committed([partition], TimeSpan.FromSeconds(10)).Single().Offset == beforeOffset, "Outage advanced committed offsets.");
        deadline = DateTime.UtcNow.AddSeconds(45);
        while (!await publisher.PublishOneAsync(ct))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Retained outbox did not become eligible after recovery.");
            await Task.Delay(250, ct);
        }
        await using (var db = database.CreateDbContext())
        {
            var published = await db.Set<KafkaOutboxMessage>().AsNoTracking().SingleAsync(ct);
            Require(published.Status == "PUBLISHED" && published.AttemptCount == 2 && published.LastErrorCode is null,
                "The original producer did not acknowledge its automatic scheduled retry.");
        }
        var text = await KafkaMetricSnapshot.ExportAsync(registry, ct);
        Require(KafkaMetricSnapshot.Value(text, "researchtrack_kafka_retry_total", ("stage", "publish")) == 1, "Retry metric missing.");
        await new KafkaOutboxMetricsWorker(new KafkaOutboxStore<TContext>(database), metrics,
            NullLogger<KafkaOutboxMetricsWorker>.Instance).SampleAsync(ct);
        await KafkaMetricSnapshot.SaveIsolatedAsync(options.Service, registry, ct);
        await VerifyHttpAsync(http, "Healthy", ct);
        await WaitForMetricAsync("researchtrack_kafka_broker_healthy", x => x == 1, ct);
        await WaitForMetricAsync("absent(ALERTS{alertname=~\"ResearchTrackKafka(Broker|Exporter)Unavailable\",alertstate=\"firing\"})", x => x == 1, ct);
        var afterSnapshot = await ControlBrokerAsync("snapshot", ct);
        using (var before = JsonDocument.Parse(beforeSnapshot))
        using (var after = JsonDocument.Parse(afterSnapshot))
        {
            foreach (var name in new[] { "mysql", "prometheus", "metric-snapshots" })
                Require(before.RootElement.GetProperty("containers").GetProperty(name).GetString() ==
                    after.RootElement.GetProperty("containers").GetProperty(name).GetString(), "Unrelated fixture dependency restarted: " + name);
        }
        await SaveEvidenceAsync(options.Service + "-recovered", registry, ct);
    }

    public static async Task FailCommitDuringOutageAsync(KafkaConsumerWorker worker,
        IConsumer<string, string> consumer, ConsumeResult<string, string> record, CancellationToken ct)
    {
        await ControlBrokerAsync("stop", ct);
        try
        {
            try { await worker.HandleAndCommitAsync(consumer, record, ct); }
            catch (KafkaOffsetCommitException) { return; }
            throw new InvalidOperationException("Offset commit unexpectedly succeeded with the disposable broker stopped.");
        }
        finally
        {
            using var recovery = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await ControlBrokerAsync("start", recovery.Token);
        }
    }

    private static async Task VerifyHttpAsync(HttpClient client, string ready, CancellationToken ct)
    {
        foreach (var path in new[] { "/health/live", "/health/ready", "/swagger/v1/swagger.json" })
        {
            using var response = await client.GetAsync(path, ct);
            Require(response.StatusCode == HttpStatusCode.OK, "Unrelated HTTP route unavailable: " + path);
            if (path == "/health/ready")
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                Require(body.RootElement.GetProperty("status").GetString() == ready, "Readiness must expose the actual messaging health state.");
            }
        }
    }

    public static async Task SaveEvidenceAsync(string phase, Prometheus.CollectorRegistry registry, CancellationToken ct)
    {
        var directory = Path.Combine(Environment.GetEnvironmentVariable("RT_KAFKA_TEST_WORK")!, "metrics");
        await File.WriteAllTextAsync(Path.Combine(directory, phase + ".evidence"), await KafkaMetricSnapshot.ExportAsync(registry, ct), ct);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var alerts = await client.GetStringAsync("http://127.0.0.1:19090/api/v1/alerts", ct);
        await File.WriteAllTextAsync(Path.Combine(directory, phase + "-alerts.json"), alerts, ct);
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
