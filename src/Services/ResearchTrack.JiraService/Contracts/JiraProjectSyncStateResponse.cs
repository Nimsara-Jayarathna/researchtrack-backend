namespace ResearchTrack.JiraService.Contracts;

public sealed record JiraProjectSyncStateResponse(
    bool Connected,
    long SyncRevision,
    string? SyncStatus,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError,
    string? WebhookStatus,
    DateTimeOffset? LastWebhookAt,
    DateTimeOffset? LastReconciledAt);
