namespace ResearchTrack.JiraService.Domain;
public sealed class JiraSprint
{
    public Guid Id { get; set; }
    public Guid JiraConnectionId { get; set; }
    public Guid ResearchProjectId { get; set; }
    public long JiraSprintId { get; set; }
    public long JiraBoardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public DateTimeOffset? CompleteDate { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
