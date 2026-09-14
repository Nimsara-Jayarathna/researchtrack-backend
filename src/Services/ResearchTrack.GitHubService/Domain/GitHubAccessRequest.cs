namespace ResearchTrack.GitHubService.Domain;

public sealed class GitHubAccessRequest
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string ProjectTitle { get; set; }
    public required string TargetOwnerLogin { get; set; }
    public Guid RequestedByUserId { get; set; }
    public required string TokenNonce { get; set; }
    public required string TokenHash { get; set; }
    public string? ResultNonce { get; set; }
    public string? ResultTokenHash { get; set; }
    public required string Status { get; set; }
    public string? PendingProjectKey { get; set; }
    public Guid? SourceId { get; set; }
    public long? InstallationId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AuthorizationStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
