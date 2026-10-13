using Prometheus;

namespace ResearchTrack.BuildingBlocks.Kafka;

public enum KafkaMetricStage
{
    Publish,
    Process,
    Commit,
}

// All labels originate here; event/provider identities and error text are never labels.
public sealed class KafkaMetrics
{
    private sealed class Families(CollectorRegistry registry)
    {
        public Counter Publish { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateCounter(
                    "researchtrack_kafka_publish_total",
                    "Kafka publication attempts by acknowledged outcome.",
                    "service",
                    "outcome"
                );
        public Counter Consume { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateCounter(
                    "researchtrack_kafka_consume_total",
                    "Durable handoff outcomes and offset-commit outcomes; handoff is not remote sync completion.",
                    "service",
                    "stage",
                    "outcome"
                );
        public Counter Retries { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateCounter(
                    "researchtrack_kafka_retry_total",
                    "Actual retry attempts, separately from final outcomes.",
                    "service",
                    "stage"
                );
        public Histogram PublishDuration { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateHistogram(
                    "researchtrack_kafka_publish_duration_seconds",
                    "Outbox publication and acknowledgement bookkeeping duration.",
                    new HistogramConfiguration
                    {
                        LabelNames = ["service"],
                        Buckets = Histogram.ExponentialBuckets(0.01, 2, 14),
                    }
                );
        public Histogram ProcessingDuration { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateHistogram(
                    "researchtrack_kafka_processing_duration_seconds",
                    "Record validation and durable handoff duration, including bounded retries.",
                    new HistogramConfiguration
                    {
                        LabelNames = ["service"],
                        Buckets = Histogram.ExponentialBuckets(0.01, 2, 14),
                    }
                );
        public Gauge Enabled { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateGauge(
                    "researchtrack_kafka_enabled",
                    "Configured application Kafka enablement, independent of broker monitoring.",
                    "service"
                );
        public Gauge Held { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateGauge(
                    "researchtrack_kafka_consumer_held",
                    "Consumer held without offset commit until review/restart.",
                    "service"
                );
        public Gauge Outbox { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateGauge(
                    "researchtrack_kafka_outbox_messages",
                    "Periodically sampled durable outbox depth; pending includes publishing leases.",
                    "service",
                    "status"
                );
        public Gauge SampleSuccess { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateGauge(
                    "researchtrack_kafka_outbox_sample_success",
                    "Whether the last bounded database sample succeeded.",
                    "service"
                );
        public Gauge SampleTimestamp { get; } =
            Metrics
                .WithCustomRegistry(registry)
                .CreateGauge(
                    "researchtrack_kafka_outbox_sample_timestamp_seconds",
                    "Unix time of the last successful outbox sample.",
                    "service"
                );
    }

    private static readonly Lazy<Families> Default = new(() => new(Metrics.DefaultRegistry));
    private readonly Families _metrics;
    public string Service { get; }

    public KafkaMetrics(string service, bool enabled, CollectorRegistry? registry = null)
    {
        if (service is not ("github" or "jira"))
            throw new ArgumentException("Kafka metric service must be github or jira.", nameof(service));
        Service = service;
        _metrics = registry is null ? Default.Value : new(registry);
        _metrics.Enabled.WithLabels(Service).Set(enabled ? 1 : 0);
        _metrics.Held.WithLabels(Service).Set(0);
        _metrics.SampleSuccess.WithLabels(Service).Set(0);
        _metrics.SampleTimestamp.WithLabels(Service).Set(0);
        foreach (var outcome in new[] { "success", "retryable_failure", "permanent_failure" })
            _metrics.Publish.WithLabels(Service, outcome).Inc(0);
        foreach (var outcome in new[] { "success", "duplicate", "noop", "failed", "rejected" })
            _metrics.Consume.WithLabels(Service, "process", outcome).Inc(0);
        foreach (var outcome in new[] { "success", "failed" })
            _metrics.Consume.WithLabels(Service, "commit", outcome).Inc(0);
        foreach (var stage in new[] { "publish", "process" })
            _metrics.Retries.WithLabels(Service, stage).Inc(0);
        foreach (var status in new[] { "pending", "failed" })
            _metrics.Outbox.WithLabels(Service, status).Set(0);
    }

    public void Published() => _metrics.Publish.WithLabels(Service, "success").Inc();

    public void PublicationFailed(bool permanent) =>
        _metrics.Publish.WithLabels(Service, permanent ? "permanent_failure" : "retryable_failure").Inc();

    public void Retry(KafkaMetricStage stage)
    {
        if (stage is not (KafkaMetricStage.Publish or KafkaMetricStage.Process))
            throw new ArgumentOutOfRangeException(nameof(stage));
        _metrics.Retries.WithLabels(Service, stage == KafkaMetricStage.Publish ? "publish" : "process").Inc();
    }

    // Called only after a new durable receipt transaction commits. Replays use Duplicate.
    public void HandedOff(bool noop) =>
        _metrics.Consume.WithLabels(Service, "process", noop ? "noop" : "success").Inc();

    public void Duplicate() => _metrics.Consume.WithLabels(Service, "process", "duplicate").Inc();

    public void ProcessingFailed(bool rejected) =>
        _metrics.Consume.WithLabels(Service, "process", rejected ? "rejected" : "failed").Inc();

    public void Committed() => _metrics.Consume.WithLabels(Service, "commit", "success").Inc();

    public void CommitFailed() => _metrics.Consume.WithLabels(Service, "commit", "failed").Inc();

    public void HoldConsumer() => _metrics.Held.WithLabels(Service).Set(1);

    public void ObservePublication(double seconds) =>
        _metrics.PublishDuration.WithLabels(Service).Observe(seconds);

    public void ObserveProcessing(double seconds) =>
        _metrics.ProcessingDuration.WithLabels(Service).Observe(seconds);

    public void OutboxSample(KafkaOutboxSnapshot snapshot)
    {
        _metrics.Outbox.WithLabels(Service, "pending").Set(snapshot.Pending);
        _metrics.Outbox.WithLabels(Service, "failed").Set(snapshot.Failed);
        _metrics.SampleSuccess.WithLabels(Service).Set(1);
        _metrics.SampleTimestamp.WithLabels(Service).Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void OutboxSampleFailed() => _metrics.SampleSuccess.WithLabels(Service).Set(0);
}
