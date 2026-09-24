namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubSyncStateResponse(
    Guid ProjectId,
    IReadOnlyList<GitHubRepositorySyncStateResponse> Repositories);

public sealed record GitHubRepositorySyncStateResponse(
    Guid LinkedRepositoryId,
    long SyncRevision,
    string SyncStatus,
    DateTime? LastSyncedAt,
    bool Enabled,
    bool Primary);
