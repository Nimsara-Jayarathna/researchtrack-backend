namespace ResearchTrack.GitHubService.Features.Installation;

public sealed record GitHubRepositoryAccessCompletionResult(
    Guid RequestId,
    Guid ProjectId,
    bool Succeeded,
    bool AlreadyCompleted,
    string? ErrorCode,
    Guid? SourceId,
    Guid? RepositoryId,
    Guid? LinkedRepositoryId,
    long? GitHubRepositoryId,
    bool InitialSyncHandoffSucceeded);
