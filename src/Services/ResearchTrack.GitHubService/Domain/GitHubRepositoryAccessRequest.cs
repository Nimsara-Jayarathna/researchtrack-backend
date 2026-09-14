namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubRepositoryAccessRequest
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid InitiatingUserId { get; set; }
    public required string RequestedOwner { get; set; }
    public required string RequestedRepositoryName { get; set; }
    public required string RequestedFullName { get; set; }
    public long? GitHubRepositoryId { get; set; }
    public required string RequestTokenHash { get; set; }
    public required string FlowType { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long? PendingInstallationId { get; set; }
    public DateTime? AuthorizationStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? FailureCode { get; set; }
    public long Version { get; set; }

}
