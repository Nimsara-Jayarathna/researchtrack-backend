using Confluent.Kafka;
using System.Text.Json;
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
            await Task.Delay(1000, ct);
        }
        throw new TimeoutException("Isolated Prometheus metric/alert condition did not converge: " + query);
    }
}
