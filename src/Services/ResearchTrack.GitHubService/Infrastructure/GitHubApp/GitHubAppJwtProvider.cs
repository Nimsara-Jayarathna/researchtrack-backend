using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubAppJwtProvider : IGitHubAppJwtProvider
{
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;

    public GitHubAppJwtProvider(GitHubAppOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public string CreateToken()
    {
        var now = _timeProvider.GetUtcNow();
        var issuedAt = now.AddSeconds(-30).ToUnixTimeSeconds();
        var expiresAt = now.AddMinutes(9).ToUnixTimeSeconds();
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            typ = "JWT"
        }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = issuedAt,
            exp = expiresAt,
            iss = _options.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        var unsignedToken = $"{header}.{payload}";

        using var rsa = RSA.Create();
        var pem = File.ReadAllText(_options.PrivateKeyPath);
        rsa.ImportFromPem(pem);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
