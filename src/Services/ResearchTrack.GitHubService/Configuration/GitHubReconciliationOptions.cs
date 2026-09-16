namespace ResearchTrack.GitHubService.Configuration;

public sealed record GitHubReconciliationOptions(TimeSpan Interval)
{
    public const int DefaultIntervalMinutes = 15;
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 1440;
}
