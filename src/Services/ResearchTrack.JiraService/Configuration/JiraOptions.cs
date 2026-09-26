namespace ResearchTrack.JiraService.Configuration;

public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string TokenEncryptionKey { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string AuthorizationUrl { get; init; } = string.Empty;
    public string TokenUrl { get; init; } = string.Empty;
    public string AccessibleResourcesUrl { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public int OAuthStateTtlMinutes { get; init; }
    public int SelectionTtlMinutes { get; init; }
    public int AtlassianTimeoutSeconds { get; init; }
    public int ProjectServiceTimeoutSeconds { get; init; }
    public string WebhookUrl { get; init; } = string.Empty;
    public int ReconciliationIntervalMinutes { get; init; } = 15;
    public int WebhookCoalesceSeconds { get; init; } = 3;
    public int SyncWorkerPollSeconds { get; init; } = 2;
}
