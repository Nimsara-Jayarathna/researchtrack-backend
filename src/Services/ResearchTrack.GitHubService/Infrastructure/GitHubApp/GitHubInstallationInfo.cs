namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed record GitHubInstallationInfo(
    long InstallationId,
    string OwnerLogin,
    string OwnerType);
