namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed record GitHubWebhookEnvelope(
    string DeliveryId,
    string EventType,
    string? Action,
    long? InstallationId,
    long? GitHubRepositoryId,
    string PayloadJson,
    string PayloadSha256);

public sealed record GitHubWebhookAcceptResult(
    Guid DeliveryRecordId,
    string DeliveryId,
    string EventType,
    bool Duplicate,
    string Status);

public sealed record GitHubWebhookProcessingPlan(
    bool Ignored,
    IReadOnlyList<Guid> LinkedRepositoryIds,
    string? Reason = null)
{
    public static GitHubWebhookProcessingPlan Ignore(string reason) => new(true, [], reason);
    public static GitHubWebhookProcessingPlan Process(IReadOnlyList<Guid> linkedRepositoryIds) =>
        new(false, linkedRepositoryIds);
}

public sealed class GitHubWebhookPermanentException : Exception
{
    public GitHubWebhookPermanentException(string message) : base(message) { }
}
