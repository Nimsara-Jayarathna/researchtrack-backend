namespace ResearchTrack.GitHubService.Features.Webhooks;

public interface IGitHubWebhookSignatureVerifier
{
    bool IsValid(ReadOnlySpan<byte> payload, string? signatureHeader);
}
