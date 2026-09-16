namespace ResearchTrack.GitHubService.Domain;

public static class GitHubInstallationFlowTypes
{
    public const string Direct = "INSTALLATION_DIRECT";
    public const string Requested = "INSTALLATION_REQUESTED";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Direct, StringComparison.Ordinal)
        || string.Equals(value, Requested, StringComparison.Ordinal);
}
