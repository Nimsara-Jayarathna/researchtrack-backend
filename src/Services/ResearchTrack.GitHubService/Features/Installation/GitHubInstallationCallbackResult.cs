namespace ResearchTrack.GitHubService.Features.Installation;

public sealed record GitHubInstallationCallbackResult(
    Guid ProjectId,
    Guid? SourceId,
    long? InstallationId,
    string FlowType,
    string ReturnPath,
    bool Succeeded,
    string? ErrorCode,
    string? ExternalRedirectUrl = null);
