using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ResearchTrack.Gateway;

namespace ResearchTrack.Gateway.Tests.Integration;

public sealed class PerformanceRateLimitPolicyTests
{
    private const string RunnerIp = "198.51.100.42";
    private const string Secret = "A-Synthetic-Test-Secret-With-Enough-Entropy-Only";

    [Theory]
    [InlineData("/api/v1/projects/01234567-89ab-cdef-0123-456789abcdef", "general", 120)]
    [InlineData("/api/v1/auth/login", "auth-login", 10)]
    [InlineData("/api/v1/auth/refresh", "auth-refresh", 30)]
    [InlineData("/api/v1/auth/forgot-password", "auth-forgot-password", 5)]
    [InlineData("/api/v1/auth/reset-password", "auth-reset-password", 10)]
    [InlineData("/api/v1/github/webhooks", "github-webhooks", 600)]
    public void Existing_buckets_and_limits_remain_unchanged(string path, string bucket, int limit)
    {
        Assert.Equal((bucket, limit), CreatePolicy().SelectBucket(path));
    }

    [Fact]
    public void Exemption_requires_matching_source_ip_and_secret()
    {
        var policy = CreatePolicy();
        Assert.True(policy.IsExempt(Context(RunnerIp, Secret)));
        Assert.True(policy.IsExempt(Context("::ffff:198.51.100.42", Secret)));
        Assert.False(policy.IsExempt(Context("198.51.100.43", Secret)));
        Assert.False(policy.IsExempt(Context(RunnerIp, "invalid-key")));
        Assert.False(policy.IsExempt(Context(RunnerIp, null)));
    }

    [Fact]
    public void Forwarded_for_header_cannot_impersonate_the_runner()
    {
        var policy = CreatePolicy();
        var context = Context("203.0.113.1", Secret);
        context.Request.Headers["X-Forwarded-For"] = RunnerIp;
        Assert.False(policy.IsExempt(context));
    }

    [Fact]
    public void Disabled_exemption_never_matches_even_from_runner()
    {
        Assert.False(CreatePolicy(enabled: false).IsExempt(Context(RunnerIp, Secret)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("not-an-ip")]
    public void Enabled_exemption_rejects_invalid_source_configuration(string ip)
    {
        Assert.Throws<InvalidOperationException>(() => CreatePolicy(ip: ip));
    }

    [Fact]
    public void Enabled_exemption_rejects_short_or_missing_token()
    {
        Assert.Throws<InvalidOperationException>(() => CreatePolicy(secret: "short"));
        Assert.Throws<InvalidOperationException>(() => CreatePolicy(secret: "CHANGE_ME"));
    }

    [Fact]
    public void Approved_vps_can_exceed_general_bucket_without_changing_login_limit()
    {
        var policy = CreatePolicy();
        using var limiter = System.Threading.RateLimiting.PartitionedRateLimiter
            .Create<HttpContext, string>(policy.Partition);
        var normal = Context("203.0.113.55", null);
        normal.Request.Path = "/api/v1/projects";
        for (var index = 0; index < 120; index++)
        {
            using var lease = limiter.AttemptAcquire(normal);
            Assert.True(lease.IsAcquired);
        }
        using (var blocked = limiter.AttemptAcquire(normal))
        {
            Assert.False(blocked.IsAcquired);
        }
        var allowed = Context(RunnerIp, Secret);
        allowed.Request.Path = "/api/v1/projects";
        for (var index = 0; index < 150; index++)
        {
            using var lease = limiter.AttemptAcquire(allowed);
            Assert.True(lease.IsAcquired);
        }
        allowed.Request.Path = "/api/v1/auth/login";
        for (var index = 0; index < 10; index++)
        {
            using var lease = limiter.AttemptAcquire(allowed);
            Assert.True(lease.IsAcquired);
        }
        using var loginBlocked = limiter.AttemptAcquire(allowed);
        Assert.False(loginBlocked.IsAcquired);
    }

    private static PerformanceRateLimitPolicy CreatePolicy(bool enabled = true,
        string ip = RunnerIp, string secret = Secret)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RateLimiting:PerformanceTest:Enabled"] = enabled.ToString(),
                ["RateLimiting:PerformanceTest:AllowedIp"] = ip,
                ["RateLimiting:PerformanceTest:Token"] = secret
            }).Build();
        return new PerformanceRateLimitPolicy(config);
    }

    private static DefaultHttpContext Context(string ip, string? secret)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (secret is not null)
        {
            context.Request.Headers[PerformanceRateLimitPolicy.AuthorizationHeader] = secret;
        }
        return context;
    }
}
