namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record InstallationAccessSourceSnapshot(
    Guid Id,
    Guid ProjectId,
    long InstallationId,
    string OwnerLogin,
    string OwnerType);
