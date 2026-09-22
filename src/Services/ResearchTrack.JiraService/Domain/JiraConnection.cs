namespace ResearchTrack.JiraService.Domain;
public sealed class JiraConnection
{
    public Guid Id { get; set; }
    public Guid ResearchProjectId { get; set; }
    public string CloudId { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string? WorkspaceUrl { get; set; }
    public string JiraProjectId { get; set; } = string.Empty;
    public string JiraProjectKey { get; set; } = string.Empty;
    public string JiraProjectName { get; set; } = string.Empty;
    public long? JiraBoardId { get; set; }
    public string? JiraBoardName { get; set; }
    public string? JiraBoardType { get; set; }
    public string AccessTokenProtected { get; set; } = string.Empty;
    public string? RefreshTokenProtected { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public string? Scope { get; set; }
    public Guid ConnectedByUserId { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public string SyncStatus { get; set; } = "PENDING";
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }
    public string WebhookStatus { get; set; } = "NOT_REGISTERED";
    public long? WebhookId { get; set; }
    public DateTimeOffset? WebhookExpiresAt { get; set; }
    public DateTimeOffset? LastWebhookAt { get; set; }
    public DateTimeOffset? LastReconciledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
