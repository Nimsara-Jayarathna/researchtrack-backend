namespace ResearchTrack.GitHubService.Contracts;

public sealed record CreateGitHubRepositoryAccessRequest(
    Guid ProjectId,
    string? RepositoryUrl);

public sealed record GitHubRepositoryAccessRequestCreateResponse(
    Guid RequestId,
    Guid ProjectId,
    string RepositoryOwner,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string Status,
    DateTime ExpiresAt,
    string RequestUrl);

public sealed record GitHubRepositoryAccessRequestMemberStatusResponse(
    Guid RequestId,
    Guid ProjectId,
    string RepositoryOwner,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string Status,
    DateTime ExpiresAt,
    string? FailureCode);
