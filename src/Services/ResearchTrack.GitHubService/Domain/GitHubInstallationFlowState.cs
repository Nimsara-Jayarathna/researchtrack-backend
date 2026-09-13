namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubInstallationFlowState
{
    public Guid Id { get; set; }
    public required string StateHash { get; set; }
    public Guid ProjectId { get; set; }
    public Guid InitiatingUserId { get; set; }
    public required string FlowType { get; set; }
    public required string ReturnPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long? PendingInstallationId { get; set; }
    public DateTime? AuthorizationStartedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
