namespace ResearchTrack.GitHubService.Features.Webhooks;

public interface IGitHubWebhookIngressService
{
    Task<GitHubWebhookAcceptResult> AcceptAsync(
        Stream body,
        long? contentLength,
        string? signature,
        string? deliveryId,
        string? eventType,
        CancellationToken cancellationToken);
}
