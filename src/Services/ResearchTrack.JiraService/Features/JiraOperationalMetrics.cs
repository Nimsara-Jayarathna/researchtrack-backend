using Prometheus;

namespace ResearchTrack.JiraService.Features;

internal static class JiraOperationalMetrics
{
    public static readonly Counter WebhookEvents = Metrics.CreateCounter(
        "jira_webhook_events_total",
        "Jira webhook events by normalized event type and outcome.",
        new CounterConfiguration { LabelNames = ["event", "outcome"] });

    public static readonly Histogram WebhookReceiveDuration = Metrics.CreateHistogram(
        "jira_webhook_receive_duration_seconds",
        "Time spent validating and durably accepting a Jira webhook delivery.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) });

    public static readonly Counter WebhookRegistration = Metrics.CreateCounter(
        "jira_webhook_registration_total",
        "Jira webhook registration lifecycle operations by operation and outcome.",
        new CounterConfiguration { LabelNames = ["operation", "outcome"] });

    public static readonly Counter SyncRequests = Metrics.CreateCounter(
        "jira_sync_requests_total",
        "Durable Jira synchronization requests by trigger and scheduling outcome.",
        new CounterConfiguration { LabelNames = ["trigger", "outcome"] });

    public static readonly Counter SyncRuns = Metrics.CreateCounter(
        "jira_sync_runs_total",
        "Jira project synchronization runs by trigger and outcome.",
        new CounterConfiguration { LabelNames = ["trigger", "outcome"] });

    public static readonly Histogram SyncDuration = Metrics.CreateHistogram(
        "jira_sync_duration_seconds",
        "Duration of Jira project synchronization runs.",
        new HistogramConfiguration
        {
            LabelNames = ["trigger"],
            Buckets = Histogram.ExponentialBuckets(0.25, 2, 14)
        });

    public static readonly Gauge SyncQueueDepth = Metrics.CreateGauge(
        "jira_sync_queue_depth",
        "Current number of durable Jira synchronization jobs by status.",
        new GaugeConfiguration { LabelNames = ["status"] });

    public static readonly Counter SyncJobOutcomes = Metrics.CreateCounter(
        "jira_sync_job_outcomes_total",
        "Durable Jira sync worker job outcomes by trigger and outcome.",
        new CounterConfiguration { LabelNames = ["trigger", "outcome"] });

    public static readonly Counter StaleJobsRecovered = Metrics.CreateCounter(
        "jira_sync_stale_jobs_recovered_total",
        "Number of stale RUNNING Jira synchronization jobs recovered by the worker.");

    public static readonly Counter ReconciliationCycles = Metrics.CreateCounter(
        "jira_reconciliation_cycles_total",
        "Jira reconciliation scheduling cycles by outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Counter ReconciliationProjects = Metrics.CreateCounter(
        "jira_reconciliation_projects_total",
        "Jira projects considered by reconciliation by outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });

    public static readonly Gauge ReconciliationLastCompleted = Metrics.CreateGauge(
        "jira_reconciliation_last_completed_timestamp_seconds",
        "Unix timestamp of the most recent completed Jira reconciliation scheduling cycle.");

    public static readonly Gauge ReconciliationLastSuccess = Metrics.CreateGauge(
        "jira_reconciliation_last_success_timestamp_seconds",
        "Unix timestamp of the most recent successful Jira reconciliation scheduling cycle.");

    public static readonly Gauge ReconciliationInterval = Metrics.CreateGauge(
        "jira_reconciliation_interval_seconds",
        "Configured Jira reconciliation interval in seconds.");

    public static readonly Gauge LastSuccessfulSync = Metrics.CreateGauge(
        "jira_sync_last_success_timestamp_seconds",
        "Unix timestamp of the most recent successful Jira synchronization by trigger.",
        new GaugeConfiguration { LabelNames = ["trigger"] });

    public static readonly Gauge SnapshotIssues = Metrics.CreateGauge(
        "jira_last_sync_issues",
        "Issue count returned by the most recent successful Jira synchronization.");

    public static readonly Gauge SnapshotSprints = Metrics.CreateGauge(
        "jira_last_sync_sprints",
        "Sprint count returned by the most recent successful Jira synchronization.");

    public static string Trigger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "UNKNOWN";
        var normalized = value.Trim().ToUpperInvariant();
        return normalized is "WEBHOOK" or "RECONCILIATION" or "MANUAL" or "COALESCED" ? normalized : "OTHER";
    }

    public static string Event(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        return value.Trim().ToLowerInvariant() switch
        {
            "jira:issue_created" or "issue_created" => "issue_created",
            "jira:issue_updated" or "issue_updated" => "issue_updated",
            "jira:issue_deleted" or "issue_deleted" => "issue_deleted",
            _ => "other"
        };
    }
}
