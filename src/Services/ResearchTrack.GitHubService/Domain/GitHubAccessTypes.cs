namespace ResearchTrack.GitHubService.Domain;

public static class GitHubAccessTypes
{
    public const string InstallationDirect = "INSTALLATION_DIRECT";
    public const string InstallationRequested = "INSTALLATION_REQUESTED";

    public static bool IsGitHubAppBacked(string? value) =>
        string.Equals(value, InstallationDirect, StringComparison.Ordinal)
        || string.Equals(value, InstallationRequested, StringComparison.Ordinal);

    public static string FromFlowType(string flowType) => flowType switch
    {
        GitHubInstallationFlowTypes.Direct => InstallationDirect,
        GitHubInstallationFlowTypes.Requested => InstallationRequested,
        _ => throw new ArgumentOutOfRangeException(nameof(flowType), "Unsupported GitHub installation flow type.")
    };
}
