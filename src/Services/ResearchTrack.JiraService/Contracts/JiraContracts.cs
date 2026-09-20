namespace ResearchTrack.JiraService.Contracts;
public sealed record JiraAuthUrlResponse(string Url);
public sealed record JiraOAuthCompleteRequest(string? Code, string? State, string? Error, string? ErrorDescription, string? SelectionToken, string? SelectedCloudId);
public sealed record JiraWorkspaceOption(string CloudId, string WorkspaceName, string? WorkspaceUrl);
public sealed record JiraProjectOption(string Id, string Key, string Name);
public sealed record JiraBoardOption(long Id, string Name, string Type);
public sealed record JiraOAuthCompleteResponse(Guid ProjectId, bool RequiresWorkspaceSelection, bool RequiresProjectSelection, string SelectionToken, IReadOnlyList<JiraWorkspaceOption> WorkspaceOptions, IReadOnlyList<JiraProjectOption> ProjectOptions, string? WorkspaceName);
public sealed record JiraBoardListResponse(IReadOnlyList<JiraBoardOption> Boards);
public sealed record LinkJiraProjectRequest(string SelectionToken, string JiraProjectId, long? JiraBoardId);
public sealed record JiraConnectionResponse(bool Connected, Guid ProjectId, string WorkspaceName, string? WorkspaceUrl, string JiraProjectId, string JiraProjectKey, string JiraProjectName, long? JiraBoardId, string? JiraBoardName, string? JiraBoardType, string SyncStatus, DateTimeOffset? LastSyncedAt, DateTimeOffset ConnectedAt);

public sealed record JiraIssueResponse(string IssueKey,string Summary,string IssueType,string Status,string? StatusCategory,string? Priority,string? AssigneeDisplayName,decimal? StoryPoints,string? ParentIssueKey,DateTimeOffset? DueDate,DateTimeOffset? UpdatedAt);
public sealed record JiraIssueSummaryResponse(int Total,int ToDo,int InProgress,int Done);
public sealed record JiraSyncStateResponse(string Status,DateTimeOffset? LastSyncedAt,string? LastSyncError);
public sealed record JiraIssueListResponse(IReadOnlyList<JiraIssueResponse> Items,JiraIssueSummaryResponse Summary,JiraSyncStateResponse Sync);
public sealed record JiraSyncResponse(int IssuesSynced,int SprintsSynced,DateTimeOffset SyncedAt);
public sealed record JiraStatusBreakdownResponse(int ToDo,int InProgress,int Done);
public sealed record JiraTypeDistributionItemResponse(string Type,int Count);
public sealed record JiraHealthResponse(int CompletionPercent,int OpenIssues,int OverdueIssues,int HighPriorityOpen,JiraStatusBreakdownResponse StatusBreakdown,IReadOnlyList<JiraTypeDistributionItemResponse> TypeDistribution,double BugRatio,DateTimeOffset? LastSyncedAt);

public sealed record JiraSprintStatusBreakdownResponse(int ToDo, int InProgress, int Done);
public sealed record JiraCurrentSprintResponse(
    long SprintId,
    string SprintName,
    string SprintState,
    string? Goal,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    DateTimeOffset? CompleteDate,
    JiraSprintStatusBreakdownResponse StatusBreakdown,
    int IssuesTotal,
    int IssuesDone,
    int IssuesRemaining,
    int CompletionPercent,
    bool SprintPointsAvailable,
    decimal SprintPointsTotal,
    decimal SprintPointsDone);
public sealed record JiraSprintProgressResponse(
    bool HasActiveSprint,
    JiraCurrentSprintResponse? ActiveSprint,
    JiraSyncStateResponse Sync);

public sealed record JiraWorkloadIssueResponse(string IssueKey, string Summary, string Status, string? StatusCategory, bool Completed);
public sealed record JiraWorkloadMemberResponse(
    string AccountId,
    string DisplayName,
    int Total,
    int Active,
    int ToDo,
    int InProgress,
    int Done,
    decimal? ActiveStoryPoints,
    IReadOnlyList<JiraWorkloadIssueResponse> Issues);
public sealed record JiraUnassignedWorkloadResponse(int Total, int Active, int Done);
public sealed record JiraWorkloadSummaryResponse(int TotalIssues, int ActiveIssues, int DoneIssues, int AssignedActiveIssues, int UnassignedActiveIssues);
public sealed record JiraWorkloadResponse(
    IReadOnlyList<JiraWorkloadMemberResponse> Members,
    JiraUnassignedWorkloadResponse Unassigned,
    JiraWorkloadSummaryResponse Summary,
    JiraSyncStateResponse Sync);
