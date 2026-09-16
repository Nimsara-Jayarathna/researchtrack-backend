namespace ResearchTrack.GitHubService.Configuration;

public static class GitHubReconciliationOptionsFactory
{
    public static GitHubReconciliationOptions Create(IConfiguration configuration)
    {
        var intervalMinutes = configuration.GetValue<int?>("GitHub:SyncIntervalMinutes")
            ?? configuration.GetValue<int?>("GitHub:Reconciliation:IntervalMinutes")
            ?? GitHubReconciliationOptions.DefaultIntervalMinutes;

        if (intervalMinutes is < GitHubReconciliationOptions.MinimumIntervalMinutes
            or > GitHubReconciliationOptions.MaximumIntervalMinutes)
        {
            throw new InvalidOperationException(
                $"GitHub synchronization configuration 'GitHub:SyncIntervalMinutes' must be between " +
                $"{GitHubReconciliationOptions.MinimumIntervalMinutes} and {GitHubReconciliationOptions.MaximumIntervalMinutes} minutes.");
        }

        return new GitHubReconciliationOptions(TimeSpan.FromMinutes(intervalMinutes));
    }
}
