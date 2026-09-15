namespace ResearchTrack.GitHubService.Contracts;

public sealed record GitHubWebhookAcceptedResponse(
    string DeliveryId,
    string EventType,
    bool Duplicate,
    string Status);
