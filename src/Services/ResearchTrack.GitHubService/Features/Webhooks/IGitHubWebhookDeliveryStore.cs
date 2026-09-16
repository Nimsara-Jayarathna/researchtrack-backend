using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public interface IGitHubWebhookDeliveryStore
{
    Task<GitHubWebhookAcceptResult> AcceptAsync(
        GitHubWebhookEnvelope envelope,
        DateTime now,
        CancellationToken cancellationToken);

    Task<GitHubWebhookDelivery?> TryClaimNextDueAsync(
        DateTime now,
        CancellationToken cancellationToken);

    Task<DateTime?> GetNextDueAtAsync(DateTime now, CancellationToken cancellationToken);

    Task RenewProcessingLeaseAsync(Guid id, DateTime now, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, bool ignored, DateTime now, CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        string error,
        DateTime now,
        DateTime? nextAttemptAt,
        CancellationToken cancellationToken);
}
