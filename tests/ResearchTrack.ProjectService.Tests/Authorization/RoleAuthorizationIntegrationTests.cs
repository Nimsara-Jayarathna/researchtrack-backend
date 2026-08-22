using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.Testing;

namespace ResearchTrack.ProjectService.Tests.Authorization;

public sealed class RoleAuthorizationIntegrationTests : IAsyncLifetime
{
    private const string Issuer = "ResearchTrack.AuthService.Tests";
    private const string Audience = "ResearchTrack.Tests";
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long-123456789";

    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public ValueTask InitializeAsync()
    {
        _factory = new ResearchTrackWebApplicationFactory<Program>(
            "Server=127.0.0.1;Port=3306;Database=unused;User=test;Password=test;SslMode=Disabled;AllowPublicKeyRetrieval=true",
            services => services
                .AddControllers()
                .AddApplicationPart(typeof(SupervisorOnlyProbeController).Assembly));
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Unauthenticated_request_to_supervisor_policy_returns_401()
    {
        var response = await Client.GetAsync(
            "/api/v1/projects/test-authorization/supervisor",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("UNAUTHORIZED", payload?.Error?.Code);
    }

    [Fact]
    public async Task Student_token_is_denied_supervisor_policy_with_403()
    {
        using var request = CreateRequest(AuthSecurityConstants.Roles.Student);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal("FORBIDDEN", payload?.Error?.Code);
    }

    [Fact]
    public async Task Supervisor_token_is_allowed_by_supervisor_policy()
    {
        using var request = CreateRequest(AuthSecurityConstants.Roles.Supervisor);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected_with_401()
    {
        using var request = CreateRequest(
            AuthSecurityConstants.Roles.Supervisor,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static HttpRequestMessage CreateRequest(string role, DateTimeOffset? expiresAt = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/projects/test-authorization/supervisor");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(role, expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15)));
        return request;
    }

    private static string CreateJwt(string role, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["role"] = role,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds()
        };

        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{encodedHeader}.{encodedPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        var signature = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");
}

[ApiController]
[Route("api/v1/projects/test-authorization")]
public sealed class SupervisorOnlyProbeController : ControllerBase
{
    [Authorize(Policy = AuthSecurityConstants.Policies.SupervisorOnly)]
    [HttpGet("supervisor")]
    public IActionResult Get() => Ok(new { allowed = true });
}
