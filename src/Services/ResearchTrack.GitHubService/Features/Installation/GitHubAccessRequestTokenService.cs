using System.Security.Cryptography;
using System.Text;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubAccessRequestTokenService : IGitHubAccessRequestTokenService
{
    private const int NonceBytes = 16;
    private readonly byte[] _key;

    public GitHubAccessRequestTokenService(GitHubAppOptions options)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.ClientSecret));
    }

    public (string Nonce, string Token, string Hash) CreateRequestToken(Guid requestId) =>
        Create(requestId, "request");

    public (string Nonce, string Token, string Hash) CreateResultToken(Guid requestId) =>
        Create(requestId, "result");

    public string RecreateRequestToken(Guid requestId, string nonce) =>
        Build(requestId, nonce, "request");

    public string RecreateResultToken(Guid requestId, string nonce) =>
        Build(requestId, nonce, "result");

    public string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public bool IsWellFormed(string token, Guid requestId, string nonce, bool resultToken)
    {
        var expected = Build(requestId, nonce, resultToken ? "result" : "request");
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(token));
    }

    private (string Nonce, string Token, string Hash) Create(Guid requestId, string purpose)
    {
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(NonceBytes));
        var token = Build(requestId, nonce, purpose);
        return (nonce, token, Hash(token));
    }

    private string Build(Guid requestId, string nonce, string purpose)
    {
        var prefix = purpose == "result" ? "gr1" : "ga1";
        var payload = $"{prefix}.{requestId:N}.{nonce}";
        using var hmac = new HMACSHA256(_key);
        var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return $"{payload}.{signature}";
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
