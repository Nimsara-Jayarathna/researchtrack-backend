using System.Security.Cryptography;
using System.Text;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Features.Webhooks;

namespace ResearchTrack.GitHubService.Tests.Features.Webhooks;

public sealed class GitHubWebhookSignatureVerifierTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void IsValid_AcceptsMatchingSha256Signature()
    {
        var payload = Encoding.UTF8.GetBytes("{\"zen\":\"Keep it logically awesome.\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
        var verifier = new GitHubWebhookSignatureVerifier(Options());
        Assert.True(verifier.IsValid(payload, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha1=deadbeef")]
    [InlineData("sha256=deadbeef")]
    public void IsValid_RejectsMalformedSignature(string? signature)
    {
        var verifier = new GitHubWebhookSignatureVerifier(Options());
        Assert.False(verifier.IsValid(Encoding.UTF8.GetBytes("{}"), signature));
    }

    [Fact]
    public void IsValid_RejectsModifiedPayload()
    {
        var original = Encoding.UTF8.GetBytes("{\"value\":1}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(original)).ToLowerInvariant();
        var verifier = new GitHubWebhookSignatureVerifier(Options());
        Assert.False(verifier.IsValid(Encoding.UTF8.GetBytes("{\"value\":2}"), signature));
    }

    private static GitHubWebhookOptions Options() => new(
        Secret,
        GitHubWebhookOptions.DefaultMaxPayloadBytes,
        GitHubWebhookOptions.DefaultMaxAttempts,
        GitHubWebhookOptions.DefaultProcessingLease,
        GitHubWebhookOptions.DefaultPollInterval);
}
