namespace ResearchTrack.JiraService.Domain;
public sealed class JiraOAuthState
{
    public Guid Id { get; set; }
    public string StateHash { get; set; } = string.Empty;
    public Guid ResearchProjectId { get; set; }
    public Guid SupervisorUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
