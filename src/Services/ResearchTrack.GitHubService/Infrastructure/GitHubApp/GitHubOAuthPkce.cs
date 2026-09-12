using System.Security.Cryptography;
using System.Text;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubOAuthPkce
{
    private readonly GitHubAppOptions _options;

    public GitHubOAuthPkce(GitHubAppOptions options)
    {
        _options = options;
    }

    public string CreateVerifier(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("OAuth state is required.", nameof(state));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.ClientSecret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(state)));
    }

    public string CreateChallenge(string state)
    {
        var verifier = CreateVerifier(state);
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
