using System.Security.Cryptography;
using System.Text;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Webhooks;

namespace ResearchTrack.GitHubService.Tests.Features.Webhooks;

public sealed class GitHubWebhookIngressServiceTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptAsync_VerifiesAndPersistsRawPayloadMetadata()
    {
        var payload = Encoding.UTF8.GetBytes("{\"action\":\"opened\",\"installation\":{\"id\":42},\"repository\":{\"id\":99}}");
        var store = new StubStore();
        var service = CreateService(store);

        var result = await service.AcceptAsync(
            new MemoryStream(payload),
            payload.Length,
            Sign(payload),
            "delivery-1",
            "pull_request",
            CancellationToken.None);

        Assert.False(result.Duplicate);
        Assert.NotNull(store.Envelope);
        Assert.Equal("opened", store.Envelope!.Action);
        Assert.Equal(42, store.Envelope.InstallationId);
        Assert.Equal(99, store.Envelope.GitHubRepositoryId);
        Assert.Equal("pull_request", store.Envelope.EventType);
    }

    [Fact]
    public async Task AcceptAsync_RejectsInvalidSignatureBeforeStore()
    {
        var payload = Encoding.UTF8.GetBytes("{}");
        var store = new StubStore();
        var service = CreateService(store);

        await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(() =>
            service.AcceptAsync(
                new MemoryStream(payload),
                payload.Length,
                "sha256=" + new string('0', 64),
                "delivery-1",
                "ping",
                CancellationToken.None));
        Assert.Null(store.Envelope);
    }

    private static GitHubWebhookIngressService CreateService(StubStore store)
    {
        var options = new GitHubWebhookOptions(
            Secret,
            GitHubWebhookOptions.DefaultMaxPayloadBytes,
            GitHubWebhookOptions.DefaultMaxAttempts,
            GitHubWebhookOptions.DefaultProcessingLease,
            GitHubWebhookOptions.DefaultPollInterval);
        return new GitHubWebhookIngressService(
            options,
            new GitHubWebhookSignatureVerifier(options),
            store,
            new GitHubWebhookSignal(),
            new FixedTimeProvider(Now));
    }

    private static string Sign(byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private sealed class StubStore : IGitHubWebhookDeliveryStore
    {
        public GitHubWebhookEnvelope? Envelope { get; private set; }

        public Task<GitHubWebhookAcceptResult> AcceptAsync(
            GitHubWebhookEnvelope envelope,
            DateTime now,
            CancellationToken cancellationToken)
        {
            Envelope = envelope;
            return Task.FromResult(new GitHubWebhookAcceptResult(
                Guid.NewGuid(),
                envelope.DeliveryId,
                envelope.EventType,
                false,
                GitHubWebhookDeliveryStatuses.Received));
        }

        public Task<GitHubWebhookDelivery?> TryClaimNextDueAsync(DateTime now, CancellationToken cancellationToken) =>
            Task.FromResult<GitHubWebhookDelivery?>(null);
        public Task<DateTime?> GetNextDueAtAsync(DateTime now, CancellationToken cancellationToken) =>
            Task.FromResult<DateTime?>(null);
        public Task RenewProcessingLeaseAsync(Guid id, DateTime now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task MarkProcessedAsync(Guid id, bool ignored, DateTime now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task MarkFailedAsync(Guid id, string error, DateTime now, DateTime? nextAttemptAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
