using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace ResearchTrack.BuildingBlocks.Kafka;

public static class KafkaClientConfiguration
{
    private static ClientConfig Common(KafkaRuntimeOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl,
        SslCaLocation = options.SslCaLocation, EnableSslCertificateVerification = true,
        SslEndpointIdentificationAlgorithm = SslEndpointIdentificationAlgorithm.Https,
        AllowAutoCreateTopics = false, SocketTimeoutMs = 10000, SocketConnectionSetupTimeoutMs = 10000
    };
    public static ProducerConfig Producer(KafkaRuntimeOptions options) => new(Common(options))
    {
        EnableIdempotence = true, Acks = Acks.All, MessageTimeoutMs = 30000,
        RequestTimeoutMs = 10000, RetryBackoffMs = 1000,
        QueueBufferingMaxMessages = 1000, QueueBufferingMaxKbytes = 1024, MessageMaxBytes = 16384
    };
    public static ConsumerConfig Consumer(KafkaRuntimeOptions options) => new(Common(options))
    {
        GroupId = options.ConsumerGroupId, EnableAutoCommit = false, EnableAutoOffsetStore = false,
        AutoOffsetReset = AutoOffsetReset.Earliest, MaxPollIntervalMs = 180000,
        SessionTimeoutMs = 30000, QueuedMaxMessagesKbytes = 1024, FetchMaxBytes = 1024 * 1024
    };
}

public interface IKafkaClientFactory
{
    IProducer<string, string> CreateProducer();
    IConsumer<string, string> CreateConsumer();
}

public sealed class KafkaClientFactory(KafkaRuntimeOptions options, ILogger<KafkaClientFactory> logger) : IKafkaClientFactory
{
    public IProducer<string, string> CreateProducer() => new ProducerBuilder<string, string>(KafkaClientConfiguration.Producer(options))
        .SetLogHandler((_, entry) => logger.LogDebug("Kafka producer client log. Level={Level}", entry.Level))
        .SetErrorHandler((_, error) => logger.LogWarning("Kafka producer client error. Code={Code}", error.Code))
        .Build();
    public IConsumer<string, string> CreateConsumer() => new ConsumerBuilder<string, string>(KafkaClientConfiguration.Consumer(options))
        .SetLogHandler((_, entry) => logger.LogDebug("Kafka consumer client log. Level={Level}", entry.Level))
        .SetErrorHandler((_, error) => logger.LogWarning("Kafka consumer client error. Code={Code}", error.Code))
        .Build();
}

public interface IKafkaEventPublisher
{
    Task PublishAsync(KafkaOutboxMessage message, CancellationToken ct);
}

public sealed class KafkaEventPublisher(IKafkaClientFactory factory, KafkaRuntimeOptions options) : IKafkaEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer = factory.CreateProducer();
    public async Task PublishAsync(KafkaOutboxMessage message, CancellationToken ct)
    {
        if (message.Topic != options.Topic) throw new KafkaContractException();
        var value = KafkaWebhookContract.Parse(message.PayloadJson, message.MessageKey, options.Service, options.ContractVersion);
        var result = await _producer.ProduceAsync(message.Topic, new Message<string, string>
        {
            Key = message.MessageKey, Value = message.PayloadJson,
            Headers = new Headers { { "correlation-id", System.Text.Encoding.UTF8.GetBytes(value.CorrelationId.ToString("D")) } }
        }, ct);
        if (result.Status != PersistenceStatus.Persisted) throw new KafkaException(new Error(ErrorCode.Local_MsgTimedOut));
    }
    public void Dispose() => _producer.Dispose();
}
