namespace ResearchTrack.JiraService.Domain;
public sealed class JiraIssue
{
    public Guid Id { get; set; }
    public Guid JiraConnectionId { get; set; }
    public Guid ResearchProjectId { get; set; }
    public string JiraIssueId { get; set; } = string.Empty;
    public string IssueKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? DescriptionJson { get; set; }
    public string? IssueTypeId { get; set; }
    public string IssueTypeName { get; set; } = "Unknown";
    public bool IsSubtask { get; set; }
    public string? StatusId { get; set; }
    public string StatusName { get; set; } = "Unknown";
    public string? StatusCategoryId { get; set; }
    public string? StatusCategoryKey { get; set; }
    public string? StatusCategoryName { get; set; }
    public string? PriorityId { get; set; }
    public string? PriorityName { get; set; }
    public string? AssigneeAccountId { get; set; }
    public string? AssigneeDisplayName { get; set; }
    public string? ReporterAccountId { get; set; }
    public string? ReporterDisplayName { get; set; }
    public decimal? StoryPoints { get; set; }
    public long? OriginalEstimateSeconds { get; set; }
    public long? RemainingEstimateSeconds { get; set; }
    public long? TimeSpentSeconds { get; set; }
    public string? ParentIssueId { get; set; }
    public string? ParentIssueKey { get; set; }
    public string? ResolutionId { get; set; }
    public string? ResolutionName { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? ResolutionDate { get; set; }
    public DateTimeOffset? JiraCreatedAt { get; set; }
    public DateTimeOffset? JiraUpdatedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
