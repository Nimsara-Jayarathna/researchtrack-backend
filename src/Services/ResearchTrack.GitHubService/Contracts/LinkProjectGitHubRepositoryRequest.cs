namespace ResearchTrack.GitHubService.Contracts;

public sealed record LinkProjectGitHubRepositoryRequest(
    long InstallationId,
    long RepositoryId);

public sealed record ProjectGitHubRepositoryLinkResponse(
    Guid ProjectId,
    long InstallationId,
    long RepositoryId,
    string Name,
    string FullName,
    string Url,
    string OwnerLogin,
    string? DefaultBranch,
    DateTime? LastSyncedAt);
