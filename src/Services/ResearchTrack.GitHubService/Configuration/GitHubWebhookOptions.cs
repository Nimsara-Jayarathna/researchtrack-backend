namespace ResearchTrack.GitHubService.Configuration;

public sealed record GitHubWebhookOptions(
    string Secret,
    int MaxPayloadBytes,
    int MaxAttempts,
    TimeSpan ProcessingLease,
    TimeSpan PollInterval)
{
    public const int DefaultMaxPayloadBytes = 1_048_576;
    public const int DefaultMaxAttempts = 5;
    public static readonly TimeSpan DefaultProcessingLease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
}
