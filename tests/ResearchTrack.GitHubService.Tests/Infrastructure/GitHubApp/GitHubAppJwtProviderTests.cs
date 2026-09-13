using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubAppJwtProviderTests
{
    [Fact]
    public void Creates_rs256_jwt_with_app_id_and_short_lifetime()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
            var now = new DateTimeOffset(2026, 9, 12, 5, 30, 0, TimeSpan.Zero);
            var provider = new GitHubAppJwtProvider(
                new GitHubAppOptions(
                    12345,
                    "researchtrack-test",
                    "Iv1.test-client",
                    "test-client-secret",
                    path,
                    new Uri("https://api.example.test/callback"),
                    new Uri("https://app.example.test/"),
                    TimeSpan.FromMinutes(10)),
                new FixedTimeProvider(now));

            var token = provider.CreateToken();
            var parts = token.Split('.');

            Assert.Equal(3, parts.Length);
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
            Assert.Equal("12345", payload.RootElement.GetProperty("iss").GetString());
            var iat = payload.RootElement.GetProperty("iat").GetInt64();
            var exp = payload.RootElement.GetProperty("exp").GetInt64();
            Assert.InRange(exp - iat, 1, 600);

            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64UrlDecode(parts[2]);
            Assert.True(rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
