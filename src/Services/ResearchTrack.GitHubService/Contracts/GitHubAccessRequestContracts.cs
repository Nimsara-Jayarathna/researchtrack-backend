namespace ResearchTrack.GitHubService.Contracts;

public sealed record CreateGitHubAccessRequestRequest(Guid ProjectId, string OwnerLogin);

public sealed record GitHubAccessRequestCreateResponse(
    Guid Id,
    Guid ProjectId,
    string OwnerLogin,
    string RequestUrl,
    string Status,
    DateTime ExpiresAt);

public sealed record GitHubAccessRequestSummaryResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectTitle,
    string OwnerLogin,
    string Status,
    string? RequestUrl,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? CompletedAt,
    DateTime? RevokedAt,
    Guid? SourceId,
    long? InstallationId,
    string? ErrorCode);

public sealed record GitHubAccessRequestValidationResponse(
    Guid ProjectId,
    string ProjectTitle,
    string OwnerLogin,
    string Status,
    DateTime ExpiresAt);

public sealed record GitHubAccessRequestContinueResponse(
    Guid ProjectId,
    string GitHubAuthorizeUrl);

public sealed record GitHubAccessUpdatedSummaryResponse(
    Guid ProjectId,
    string ProjectTitle,
    long InstallationId,
    Guid? SourceId,
    string FlowType,
    string AccessScope,
    int AccessibleRepositoryCount,
    IReadOnlyList<GitHubInstallationRepositoryResponse> Repositories);

public sealed record GitHubAccessUpdatedAcknowledgeResponse(Guid ProjectId);
