using Confluent.Kafka;
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
}
