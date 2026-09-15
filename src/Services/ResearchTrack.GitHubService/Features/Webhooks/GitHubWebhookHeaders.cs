namespace ResearchTrack.GitHubService.Features.Webhooks;

public static class GitHubWebhookHeaders
{
    public const string Signature256 = "X-Hub-Signature-256";
    public const string Delivery = "X-GitHub-Delivery";
    public const string Event = "X-GitHub-Event";
}
