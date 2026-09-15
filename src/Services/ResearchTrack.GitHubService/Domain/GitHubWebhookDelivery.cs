namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubWebhookDelivery
{
    public Guid Id { get; set; }
    public required string DeliveryId { get; set; }
    public required string EventType { get; set; }
    public string? Action { get; set; }
    public long? InstallationId { get; set; }
    public long? GitHubRepositoryId { get; set; }
    public required string PayloadJson { get; set; }
    public required string PayloadSha256 { get; set; }
    public required string Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
}
