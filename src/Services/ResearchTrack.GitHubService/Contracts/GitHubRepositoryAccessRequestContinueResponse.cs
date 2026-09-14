namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubRepositoryAccessRequestContinueResponse(
    Guid RequestId,
    string GitHubAuthorizeUrl,
    DateTime ExpiresAt);
