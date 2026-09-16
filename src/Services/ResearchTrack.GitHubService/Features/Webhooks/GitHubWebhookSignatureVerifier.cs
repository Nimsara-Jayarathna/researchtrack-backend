using System.Security.Cryptography;
using System.Text;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Features.Webhooks;

public sealed class GitHubWebhookSignatureVerifier : IGitHubWebhookSignatureVerifier
{
    private const string Prefix = "sha256=";
    private readonly byte[] _secret;

    public GitHubWebhookSignatureVerifier(GitHubWebhookOptions options)
    {
        _secret = Encoding.UTF8.GetBytes(options.Secret);
    }

    public bool IsValid(ReadOnlySpan<byte> payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hex = signatureHeader[Prefix.Length..].Trim();
        if (hex.Length != 64)
        {
            return false;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        if (supplied.Length != 32)
        {
            return false;
        }

        using var hmac = new HMACSHA256(_secret);
        Span<byte> expected = stackalloc byte[32];
        if (!hmac.TryComputeHash(payload, expected, out var expectedWritten)
            || expectedWritten != expected.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
