using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace ResearchTrack.Gateway;

/// <summary>
/// Explicitly approved load-generation source. A public IP alone is not an
/// authentication factor: both the true forwarded client IP and a secret header
/// must match. Only the general API bucket can be exempted.
/// </summary>
public sealed class PerformanceRateLimitPolicy
{
    public const string AuthorizationHeader = "X-ResearchTrack-Performance-Key";

    private readonly IPAddress? _allowedIp;
    private readonly byte[]? _tokenBytes;

    public bool Enabled { get; }

    public PerformanceRateLimitPolicy(IConfiguration configuration)
    {
        Enabled = configuration.GetValue<bool>("RateLimiting:PerformanceTest:Enabled");
        if (!Enabled)
        {
            return;
        }

        var ip = configuration["RateLimiting:PerformanceTest:AllowedIp"];
        var token = configuration["RateLimiting:PerformanceTest:Token"];
        if (!IPAddress.TryParse(ip, out var allowedIp) ||
            IPAddress.Any.Equals(allowedIp) || IPAddress.IPv6Any.Equals(allowedIp) ||
            IPAddress.IsLoopback(allowedIp))
        {
            throw new InvalidOperationException(
                "RateLimiting:PerformanceTest:AllowedIp must be one explicit, non-loopback IP address.");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length < 32 ||
            token.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RateLimiting:PerformanceTest:Token must be a non-placeholder secret of at least 32 characters.");
        }

        _allowedIp = Normalize(allowedIp);
        _tokenBytes = Encoding.UTF8.GetBytes(token);
    }

    public bool IsExempt(HttpContext context)
    {
        if (!Enabled || _allowedIp is null || _tokenBytes is null ||
            context.Connection.RemoteIpAddress is not { } remote ||
            !Normalize(remote).Equals(_allowedIp))
        {
            return false;
        }

        var provided = context.Request.Headers[AuthorizationHeader];
        if (provided.Count != 1 || string.IsNullOrEmpty(provided[0]))
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(provided[0]!);
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, _tokenBytes);
    }

    public (string Bucket, int PermitLimit) SelectBucket(string path) => path switch
    {
        "/api/v1/auth/login" => ("auth-login", 10),
        "/api/v1/auth/refresh" => ("auth-refresh", 30),
        "/api/v1/auth/forgot-password" => ("auth-forgot-password", 5),
        "/api/v1/auth/reset-password" => ("auth-reset-password", 10),
        "/api/github/webhooks" or "/api/v1/github/webhooks" => ("github-webhooks", 600),
        _ => ("general", 120)
    };

    public RateLimitPartition<string> Partition(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (bucket, permits) = SelectBucket(context.Request.Path.Value ?? string.Empty);
        if (bucket == "general" && IsExempt(context))
        {
            // The specially authorized VPS measures backend capacity without
            // the normal per-IP general bucket masking the result with 429s.
            return RateLimitPartition.GetNoLimiter($"performance:{remoteIp}");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{bucket}:{remoteIp}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
