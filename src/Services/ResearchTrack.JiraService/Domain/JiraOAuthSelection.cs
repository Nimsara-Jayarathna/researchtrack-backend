namespace ResearchTrack.JiraService.Domain;
public sealed class JiraOAuthSelection
{
    public Guid Id { get; set; }
    public string SelectionTokenHash { get; set; } = string.Empty;
    public Guid ResearchProjectId { get; set; }
    public Guid SupervisorUserId { get; set; }
    public string AccessTokenProtected { get; set; } = string.Empty;
    public string? RefreshTokenProtected { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string WorkspacesJson { get; set; } = "[]";
    public string? SelectedCloudId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
