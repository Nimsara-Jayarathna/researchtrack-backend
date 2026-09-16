namespace ResearchTrack.GitHubService.Features.Installation;

public sealed record GitHubInstallationState(
    string Value,
    DateTime ExpiresAt);

public sealed record ValidatedGitHubInstallationState(
    Guid ProjectId,
    Guid InitiatingUserId,
    string FlowType,
    string ReturnPath,
    Guid? AccessRequestId,
    long? PendingInstallationId,
    DateTime? AuthorizationStartedAt);

public sealed record ConsumedGitHubInstallationState(
    Guid ProjectId,
    Guid InitiatingUserId,
    string FlowType,
    string ReturnPath,
    Guid? AccessRequestId,
    long? PendingInstallationId,
    DateTime ConsumedAt);
