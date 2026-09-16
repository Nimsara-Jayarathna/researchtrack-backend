namespace ResearchTrack.GitHubService.Features.Installation;

public sealed record LegacyInstallationRepositorySelection(
    Guid SourceId,
    Guid RepositoryId,
    long GitHubRepositoryId);
