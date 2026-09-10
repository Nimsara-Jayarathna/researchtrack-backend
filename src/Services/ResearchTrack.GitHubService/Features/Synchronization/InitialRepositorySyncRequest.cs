namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed record InitialRepositorySyncRequest(
    Guid ProjectId,
    Guid LinkedRepositoryId,
    Guid SourceId,
    Guid GitHubRepositoryId,
    long GitHubRepoId);
