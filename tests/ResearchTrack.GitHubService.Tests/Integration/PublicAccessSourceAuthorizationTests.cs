using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.Testing;

namespace ResearchTrack.GitHubService.Tests.Integration;

public sealed class PublicAccessSourceAuthorizationTests : IAsyncLifetime
{
    private const string Issuer = "ResearchTrack.AuthService.Tests";
    private const string Audience = "ResearchTrack.Tests";
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long-123456789";
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RepositoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private StubPublicAccessSourceService? _service;
    private StubRepositoryLinkService? _linkService;

    public ValueTask InitializeAsync()
    {
        _service = new StubPublicAccessSourceService();
        _linkService = new StubRepositoryLinkService();
        _factory = new ResearchTrackWebApplicationFactory<Program>(
            TestDatabaseConfiguration.NonConnectingPlaceholder,
            services =>
            {
                services.RemoveAll<IPublicAccessSourceService>();
                services.AddSingleton<IPublicAccessSourceService>(_service);
                services.RemoveAll<IRepositoryLinkService>();
                services.AddSingleton<IRepositoryLinkService>(_linkService);
            });
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Unauthenticated_user_is_rejected()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/github/access-source/public",
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(Service.WasCalled);
    }

    [Fact]
    public async Task Student_is_not_authorized_to_manage_github_access()
    {
        using var request = CreateRequest(AuthSecurityConstants.Roles.Student);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(Service.WasCalled);
    }

    [Fact]
    public async Task Authorized_supervisor_receives_exact_frontend_contract()
    {
        using var request = CreateRequest(AuthSecurityConstants.Roles.Supervisor);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(SourceId.ToString(), data.GetProperty("sourceId").GetString());
        Assert.Equal(1296269, data.GetProperty("items")[0].GetProperty("githubRepoId").GetInt64());
        var envelope = await response.Content.ReadFromJsonAsync<
            ApiResponse<GitHubAvailableRepositoriesResponse>>(TestContext.Current.CancellationToken);
        Assert.True(envelope?.Success);
        Assert.Equal(SourceId, envelope!.Data?.SourceId);
        Assert.Equal(1, envelope.Data?.TotalCount);
        Assert.Equal(RepositoryId, Assert.Single(envelope.Data!.Items).Id);
        Assert.Equal(UserId, Service.UserId);
    }

    [Fact]
    public async Task Unauthenticated_repository_link_is_rejected()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/github/repositories/link",
            ValidLinkRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(LinkService.WasCalled);
    }

    [Fact]
    public async Task Authorized_supervisor_receives_exact_repository_link_contract()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/repositories/link")
        {
            Content = JsonContent.Create(ValidLinkRequest())
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(AuthSecurityConstants.Roles.Supervisor));

        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(ProjectId.ToString(), data.GetProperty("projectId").GetString());
        var repository = data.GetProperty("repositories")[0];
        Assert.Equal(RepositoryId.ToString(), repository.GetProperty("githubRepositoryId").GetString());
        Assert.Equal(1296269, repository.GetProperty("githubRepoId").GetInt64());
        Assert.Equal("PENDING", repository.GetProperty("syncStatus").GetString());
        Assert.Equal(UserId, LinkService.UserId);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("Client is unavailable.");
    private StubPublicAccessSourceService Service =>
        _service ?? throw new InvalidOperationException("Service is unavailable.");
    private StubRepositoryLinkService LinkService =>
        _linkService ?? throw new InvalidOperationException("Link service is unavailable.");

    private static CreatePublicAccessSourceRequest ValidRequest() => new(
        ProjectId,
        "https://github.com/openai/example");

    private static LinkGitHubRepositoriesRequest ValidLinkRequest() => new(
        ProjectId,
        SourceId,
        [new LinkGitHubRepositoryRequestItem(RepositoryId, "Research repository", true)]);

    private static HttpRequestMessage CreateRequest(string role)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/access-source/public")
        {
            Content = JsonContent.Create(ValidRequest())
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role));
        return request;
    }

    private static string CreateToken(string role)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["sub"] = UserId.ToString(),
            ["role"] = role,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        return $"{unsigned}.{Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)))}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed class StubPublicAccessSourceService : IPublicAccessSourceService
    {
        public bool WasCalled { get; private set; }
        public Guid? UserId { get; private set; }

        public Task<GitHubAvailableRepositoriesResponse> CreateAsync(
            Guid userId,
            CreatePublicAccessSourceRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            return Task.FromResult(new GitHubAvailableRepositoriesResponse(
                SourceId,
                [new GitHubRepositoryOptionResponse(
                    RepositoryId,
                    1296269,
                    "openai/example",
                    "example",
                    "openai",
                    "main",
                    "https://github.com/openai/example")],
                1));
        }
    }

    private sealed class StubRepositoryLinkService : IRepositoryLinkService
    {
        public bool WasCalled { get; private set; }
        public Guid? UserId { get; private set; }

        public Task<ProjectGitHubRepositoriesResponse> LinkAsync(
            Guid userId,
            LinkGitHubRepositoriesRequest request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            UserId = userId;
            return Task.FromResult(new ProjectGitHubRepositoriesResponse(
                ProjectId,
                5,
                5,
                [],
                [new ProjectRepositoryLinkResponse(
                    Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    SourceId,
                    "PUBLIC_URL",
                    RepositoryId,
                    1296269,
                    "openai/example",
                    "example",
                    "Research repository",
                    "openai",
                    "main",
                    "https://github.com/openai/example",
                    true,
                    true,
                    new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
                    null,
                    "PENDING")]));
        }
    }

}
