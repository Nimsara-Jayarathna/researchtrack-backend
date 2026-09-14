namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubRepositoryAccessRequestStatusResponse(
    Guid RequestId,
    string RepositoryOwner,
    string RepositoryName,
    string RepositoryFullName,
    string RepositoryUrl,
    string Status,
    DateTime ExpiresAt,
    string? FailureCode);
