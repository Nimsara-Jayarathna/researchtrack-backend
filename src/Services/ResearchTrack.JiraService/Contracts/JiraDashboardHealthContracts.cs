namespace ResearchTrack.JiraService.Contracts;

public sealed record JiraDashboardHealthRequest(
    IReadOnlyList<Guid> ProjectIds);

public sealed record JiraDashboardProjectHealthResponse(
    Guid ProjectId,
    bool Connected,
    string Indicator,
    int CompletionPercent,
    int OpenIssues,
    int OverdueIssues,
    int HighPriorityOpen,
    string? SyncStatus,
    DateTimeOffset? LastSyncedAt);

public sealed record JiraDashboardHealthResponse(
    int ConnectedCount,
    int AtRiskCount,
    int BehindCount,
    IReadOnlyList<JiraDashboardProjectHealthResponse> Projects);
