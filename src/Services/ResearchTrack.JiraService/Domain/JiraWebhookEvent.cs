namespace ResearchTrack.JiraService.Domain;

public sealed class JiraWebhookEvent
{
    public Guid Id { get; set; }
    public string DeliveryId { get; set; } = string.Empty;
    public string CloudId { get; set; } = string.Empty;
    public Guid? ResearchProjectId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? JiraIssueId { get; set; }
    public string? IssueKey { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "RECEIVED";
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
