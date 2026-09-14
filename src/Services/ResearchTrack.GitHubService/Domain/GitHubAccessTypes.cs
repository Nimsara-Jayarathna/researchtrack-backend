namespace ResearchTrack.GitHubService.Domain;

public static class GitHubAccessTypes
{
    public const string PublicUrl = "PUBLIC_URL";
    public const string InstallationDirect = "INSTALLATION_DIRECT";
    public const string InstallationRequested = "INSTALLATION_REQUESTED";

    // Server-side implementation alias retained for the original direct installation flow.
    public const string GitHubApp = InstallationDirect;

    public static bool IsInstallationBacked(string? accessType) =>
        string.Equals(accessType, InstallationDirect, StringComparison.Ordinal)
        || string.Equals(accessType, InstallationRequested, StringComparison.Ordinal);

    // Keep installation identity independent from how the repository link was initiated.
    // Existing direct sources already use INSTALLATION_DIRECT in this persisted uniqueness key.
    public static string BuildActiveInstallationKey(Guid projectId, long installationId) =>
        $"{projectId:N}:{InstallationDirect}:{installationId}";
}
