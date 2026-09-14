using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed record OwnerGrantedRepositoryCompletionPersistenceResult(
    Guid RequestId,
    Guid ProjectId,
    bool Succeeded,
    bool AlreadyCompleted,
    string? ErrorCode,
    Guid? SourceId,
    Guid? RepositoryId,
    Guid? LinkedRepositoryId,
    long? GitHubRepositoryId,
    InitialRepositorySyncRequest? InitialSyncRequest);
