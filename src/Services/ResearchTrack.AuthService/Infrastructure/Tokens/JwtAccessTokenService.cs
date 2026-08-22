using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.AuthService.Domain;

namespace ResearchTrack.AuthService.Infrastructure.Tokens;

public sealed class JwtAccessTokenService : IAccessTokenService
{
    private readonly JwtOptions _options;

    public JwtAccessTokenService(JwtOptions options) => _options = options;

    public string Generate(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["role"] = user.Role == UserRole.Student ? "STUDENT" : "SUPERVISOR",
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(_options.AccessTokenMinutes).ToUnixTimeSeconds()
        };

        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{encodedHeader}.{encodedPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        var signature = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
