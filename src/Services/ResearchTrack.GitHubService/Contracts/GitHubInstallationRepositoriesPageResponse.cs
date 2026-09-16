namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubInstallationRepositoryResponse(
    long RepositoryId,
    string Name,
    string FullName,
    string Url,
    string OwnerLogin,
    string? DefaultBranch);

public sealed record GitHubInstallationRepositoriesPageResponse(
    IReadOnlyList<GitHubInstallationRepositoryResponse> Items,
    int Page,
    int Size,
    int ReturnedCount,
    int? TotalCount,
    bool HasNext,
    bool HasPrevious,
    int? NextPage);
