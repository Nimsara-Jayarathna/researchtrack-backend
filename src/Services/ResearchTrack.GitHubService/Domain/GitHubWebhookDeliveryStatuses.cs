namespace ResearchTrack.GitHubService.Domain;

public static class GitHubWebhookDeliveryStatuses
{
    public const string Received = "RECEIVED";
    public const string Processing = "PROCESSING";
    public const string Processed = "PROCESSED";
    public const string Ignored = "IGNORED";
    public const string Failed = "FAILED";
}
