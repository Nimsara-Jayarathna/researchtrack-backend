namespace ResearchTrack.GitHubService.Domain;

public static class GitHubConnectionStatuses
{
    public const string Connected = "CONNECTED";
    public const string Suspended = "SUSPENDED";
    public const string Removed = "REMOVED";
}

public static class GitHubRepositoryAccessStatuses
{
    public const string Available = "AVAILABLE";
    public const string RepositoryAccessRevoked = "REPOSITORY_ACCESS_REVOKED";
    public const string InstallationSuspended = "INSTALLATION_SUSPENDED";
    public const string InstallationRemoved = "INSTALLATION_REMOVED";
}
