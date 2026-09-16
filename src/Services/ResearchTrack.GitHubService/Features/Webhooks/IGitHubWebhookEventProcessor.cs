using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public interface IGitHubWebhookEventProcessor
{
    Task<GitHubWebhookProcessingPlan> ProcessAsync(
        GitHubWebhookDelivery delivery,
        CancellationToken cancellationToken);
}
