namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed record GitHubInstallationRepository(
    long Id,
    string OwnerLogin,
    string Name,
    string FullName,
    string HtmlUrl,
    string? DefaultBranch,
    bool Private);

public sealed record GitHubInstallationRepositoryPage(
    IReadOnlyList<GitHubInstallationRepository> Items,
    int TotalCount,
    bool HasNext);
