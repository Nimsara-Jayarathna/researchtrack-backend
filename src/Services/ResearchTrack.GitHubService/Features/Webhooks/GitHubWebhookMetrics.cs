using Prometheus;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public static class GitHubWebhookMetrics
{
    public static readonly Counter Received = Metrics.CreateCounter(
        "researchtrack_github_webhooks_received_total",
        "GitHub webhook deliveries accepted by event type.",
        new CounterConfiguration { LabelNames = ["event"] });

    public static readonly Counter Rejected = Metrics.CreateCounter(
        "researchtrack_github_webhooks_rejected_total",
        "GitHub webhook deliveries rejected before persistence.",
        new CounterConfiguration { LabelNames = ["reason"] });

    public static readonly Counter Processed = Metrics.CreateCounter(
        "researchtrack_github_webhooks_processed_total",
        "GitHub webhook processing outcomes.",
        new CounterConfiguration { LabelNames = ["event", "status"] });

    public static readonly Histogram ProcessingDuration = Metrics.CreateHistogram(
        "researchtrack_github_webhook_processing_duration_seconds",
        "Time spent processing one durable GitHub webhook delivery.",
        new HistogramConfiguration
        {
            LabelNames = ["event"],
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 12)
        });
}
