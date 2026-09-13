namespace ResearchTrack.GitHubService.Domain;

public static class GitHubSyncRunStatuses
{
    public const string Running = "RUNNING";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}

public static class GitHubSyncTriggers
{
    public const string InitialLink = "INITIAL_LINK";
    public const string Manual = "MANUAL";
    public const string Scheduled = "SCHEDULED";
    public const string Webhook = "WEBHOOK";
}
