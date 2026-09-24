namespace ResearchTrack.JiraService.Domain;

public sealed class JiraSyncJob
{
    public Guid Id { get; set; }
    public Guid ResearchProjectId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Scope { get; set; } = "FULL_PROJECT";
    public string? EntityId { get; set; }
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}
