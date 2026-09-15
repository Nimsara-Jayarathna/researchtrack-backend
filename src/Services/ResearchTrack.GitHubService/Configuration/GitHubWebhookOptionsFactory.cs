namespace ResearchTrack.GitHubService.Configuration;

public static class GitHubWebhookOptionsFactory
{
    public static GitHubWebhookOptions Create(IConfiguration configuration)
    {
        var secret = Require(configuration, "GitHub:WebhookSecret");
        if (secret.Length < 32)
        {
            throw Invalid("GitHub:WebhookSecret", "must contain at least 32 characters of high-entropy secret material.");
        }

        var maxPayloadBytes = configuration.GetValue<int?>("GitHub:Webhook:MaxPayloadBytes")
            ?? GitHubWebhookOptions.DefaultMaxPayloadBytes;
        if (maxPayloadBytes is < 16_384 or > 10_485_760)
        {
            throw Invalid("GitHub:Webhook:MaxPayloadBytes", "must be between 16384 and 10485760 bytes.");
        }

        var maxAttempts = configuration.GetValue<int?>("GitHub:Webhook:MaxAttempts")
            ?? GitHubWebhookOptions.DefaultMaxAttempts;
        if (maxAttempts is < 1 or > 10)
        {
            throw Invalid("GitHub:Webhook:MaxAttempts", "must be between 1 and 10.");
        }

        var leaseSeconds = configuration.GetValue<int?>("GitHub:Webhook:ProcessingLeaseSeconds")
            ?? (int)GitHubWebhookOptions.DefaultProcessingLease.TotalSeconds;
        if (leaseSeconds is < 30 or > 3600)
        {
            throw Invalid("GitHub:Webhook:ProcessingLeaseSeconds", "must be between 30 and 3600 seconds.");
        }

        var pollSeconds = configuration.GetValue<int?>("GitHub:Webhook:PollIntervalSeconds")
            ?? (int)GitHubWebhookOptions.DefaultPollInterval.TotalSeconds;
        if (pollSeconds is < 1 or > 60)
        {
            throw Invalid("GitHub:Webhook:PollIntervalSeconds", "must be between 1 and 60 seconds.");
        }

        return new GitHubWebhookOptions(
            secret,
            maxPayloadBytes,
            maxAttempts,
            TimeSpan.FromSeconds(leaseSeconds),
            TimeSpan.FromSeconds(pollSeconds));
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || value.Equals("__SET_ME__", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(key, "is required.");
        }
        return value;
    }

    private static InvalidOperationException Invalid(string key, string detail) =>
        new($"Required GitHub webhook configuration '{key}' {detail}");
}
