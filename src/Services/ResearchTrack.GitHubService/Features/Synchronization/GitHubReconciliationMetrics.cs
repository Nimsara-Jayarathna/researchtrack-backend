using Prometheus;

namespace ResearchTrack.GitHubService.Features.Synchronization;

internal static class GitHubReconciliationMetrics
{
    public static readonly Counter CyclesTotal = Metrics.CreateCounter(
        "github_reconciliation_cycles_total",
        "Total number of scheduled GitHub reconciliation cycles by outcome.",
        new CounterConfiguration { LabelNames = new[] { "outcome" } });

    public static readonly Counter ChecksTotal = Metrics.CreateCounter(
        "github_reconciliation_checks_total",
        "Total number of linked repository reconciliation checks by outcome.",
        new CounterConfiguration { LabelNames = new[] { "outcome" } });

    public static readonly Counter QueuedTotal = Metrics.CreateCounter(
        "github_reconciliation_repositories_queued_total",
        "Total number of linked repositories queued for synchronization by reconciliation.");

    public static readonly Gauge LastCompletedTimestampSeconds = Metrics.CreateGauge(
        "github_reconciliation_last_completed_timestamp_seconds",
        "Unix timestamp of the most recent completed reconciliation cycle, including partial-failure cycles.");

    public static readonly Gauge LastSuccessTimestampSeconds = Metrics.CreateGauge(
        "github_reconciliation_last_success_timestamp_seconds",
        "Unix timestamp of the most recent reconciliation cycle where every eligible repository check succeeded.");

    public static readonly Gauge IntervalSeconds = Metrics.CreateGauge(
        "github_reconciliation_interval_seconds",
        "Configured interval between scheduled GitHub reconciliation cycles in seconds.");
}
