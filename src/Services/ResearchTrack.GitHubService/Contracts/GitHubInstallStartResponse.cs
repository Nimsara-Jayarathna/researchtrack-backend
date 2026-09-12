namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubInstallStartResponse(
    Guid ProjectId,
    string GitHubAuthorizeUrl,
    string FlowType,
    DateTime ExpiresAt);
