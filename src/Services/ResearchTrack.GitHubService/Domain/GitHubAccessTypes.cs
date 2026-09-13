namespace ResearchTrack.GitHubService.Domain;

public static class GitHubAccessTypes
{
    public const string PublicUrl = "PUBLIC_URL";
    public const string InstallationDirect = "INSTALLATION_DIRECT";
    public const string InstallationRequested = "INSTALLATION_REQUESTED";

    // Server-side implementation alias: direct installation sources are backed by the GitHub App.
    public const string GitHubApp = InstallationDirect;
}
