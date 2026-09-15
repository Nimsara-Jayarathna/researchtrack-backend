using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookIngressService : IGitHubWebhookIngressService
{
    private readonly GitHubWebhookOptions _options;
    private readonly IGitHubWebhookSignatureVerifier _signatureVerifier;
    private readonly IGitHubWebhookDeliveryStore _store;
    private readonly GitHubWebhookSignal _signal;
    private readonly TimeProvider _timeProvider;

    public GitHubWebhookIngressService(
        GitHubWebhookOptions options,
        IGitHubWebhookSignatureVerifier signatureVerifier,
        IGitHubWebhookDeliveryStore store,
        GitHubWebhookSignal signal,
        TimeProvider timeProvider)
    {
        _options = options;
        _signatureVerifier = signatureVerifier;
        _store = store;
        _signal = signal;
        _timeProvider = timeProvider;
    }

    public async Task<GitHubWebhookAcceptResult> AcceptAsync(
        Stream body,
        long? contentLength,
        string? signature,
        string? deliveryId,
        string? eventType,
        CancellationToken cancellationToken)
    {
        deliveryId = NormalizeHeader(deliveryId);
        eventType = NormalizeHeader(eventType)?.ToLowerInvariant();
        if (deliveryId is null || deliveryId.Length > 128)
        {
            GitHubWebhookMetrics.Rejected.WithLabels("missing_delivery").Inc();
            throw BadRequest("GitHub webhook delivery id is required.");
        }
        if (eventType is null || eventType.Length > 64 || !eventType.All(IsEventCharacter))
        {
            GitHubWebhookMetrics.Rejected.WithLabels("invalid_event").Inc();
            throw BadRequest("GitHub webhook event type is invalid.");
        }
        if (contentLength is > 0 && contentLength > _options.MaxPayloadBytes)
        {
            GitHubWebhookMetrics.Rejected.WithLabels("payload_too_large").Inc();
            throw new ApiException(
                StatusCodes.Status413PayloadTooLarge,
                ErrorCodes.BadRequest,
                "GitHub webhook payload exceeds the configured size limit.");
        }

        var payload = await ReadBoundedAsync(body, _options.MaxPayloadBytes, cancellationToken);
        if (!_signatureVerifier.IsValid(payload, signature))
        {
            GitHubWebhookMetrics.Rejected.WithLabels("invalid_signature").Inc();
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "GitHub webhook signature validation failed.");
        }

        string payloadJson;
        string? action;
        long? installationId;
        long? repositoryId;
        try
        {
            payloadJson = Encoding.UTF8.GetString(payload);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            action = ReadString(root, "action");
            installationId = ReadNestedInt64(root, "installation", "id");
            repositoryId = ReadNestedInt64(root, "repository", "id");
        }
        catch (JsonException exception)
        {
            GitHubWebhookMetrics.Rejected.WithLabels("invalid_json").Inc();
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "GitHub webhook payload is not valid JSON.",
                innerException: exception);
        }

        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var envelope = new GitHubWebhookEnvelope(
            deliveryId,
            eventType,
            action,
            installationId,
            repositoryId,
            payloadJson,
            hash);
        var result = await _store.AcceptAsync(
            envelope,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        GitHubWebhookMetrics.Received.WithLabels(eventType).Inc();
        _signal.Pulse();
        return result;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream body,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
            {
                GitHubWebhookMetrics.Rejected.WithLabels("payload_too_large").Inc();
                throw new ApiException(
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorCodes.BadRequest,
                    "GitHub webhook payload exceeds the configured size limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private static string? NormalizeHeader(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsEventCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-';

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadNestedInt64(JsonElement root, string parent, string child)
    {
        if (!root.TryGetProperty(parent, out var parentValue)
            || parentValue.ValueKind != JsonValueKind.Object
            || !parentValue.TryGetProperty(child, out var childValue)
            || !childValue.TryGetInt64(out var result))
        {
            return null;
        }
        return result;
    }

    private static ApiException BadRequest(string message) => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.BadRequest,
        message);
}
