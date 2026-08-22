using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Persistence;
using ResearchTrack.Testing;

namespace ResearchTrack.ProjectService.Tests.Integration;

public sealed class ProjectIntegrationTests : IAsyncLifetime
{
    private const string Issuer = "ResearchTrack.AuthService.Tests";
    private const string Audience = "ResearchTrack.Tests";
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long-123456789";
    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("PROJECT");
        _factory = new ResearchTrackWebApplicationFactory<Program>(connectionString);
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_can_create_project_and_owner_comes_from_jwt_subject()
    {
        var supervisorId = Guid.NewGuid();
        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", supervisorId, AuthSecurityConstants.Roles.Supervisor, ValidCreateRequest());
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload?.Data);
        Assert.Equal("PLANNING", payload!.Data!.LifecycleStatus);
        Assert.Equal(0, payload.Data.ProgressPercent);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var stored = await dbContext.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(supervisorId, stored.SupervisorUserId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_cannot_create_project_and_no_row_is_created()
    {
        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", Guid.NewGuid(), AuthSecurityConstants.Roles.Student, ValidCreateRequest());
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await dbContext.Projects.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Created_project_appears_only_in_owning_supervisors_collection()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        using (var create = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", owner, AuthSecurityConstants.Roles.Supervisor, ValidCreateRequest()))
        {
            Assert.Equal(HttpStatusCode.Created, (await Client.SendAsync(create, TestContext.Current.CancellationToken)).StatusCode);
        }

        using var ownerRequest = CreateRequest(HttpMethod.Get, "/api/v1/projects", owner, AuthSecurityConstants.Roles.Supervisor);
        var ownerResponse = await Client.SendAsync(ownerRequest, TestContext.Current.CancellationToken);
        var ownerPayload = await ownerResponse.Content.ReadFromJsonAsync<ApiResponse<List<ProjectSummaryResponse>>>(TestContext.Current.CancellationToken);
        Assert.Single(ownerPayload?.Data ?? []);

        using var otherRequest = CreateRequest(HttpMethod.Get, "/api/v1/projects", other, AuthSecurityConstants.Roles.Supervisor);
        var otherResponse = await Client.SendAsync(otherRequest, TestContext.Current.CancellationToken);
        var otherPayload = await otherResponse.Content.ReadFromJsonAsync<ApiResponse<List<ProjectSummaryResponse>>>(TestContext.Current.CancellationToken);
        Assert.Empty(otherPayload?.Data ?? []);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Other_supervisor_gets_not_found_for_project_by_id()
    {
        var owner = Guid.NewGuid();
        using var create = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", owner, AuthSecurityConstants.Roles.Supervisor, ValidCreateRequest());
        var createResponse = await Client.SendAsync(create, TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(TestContext.Current.CancellationToken);
        Assert.NotNull(created?.Data);

        using var read = CreateRequest(HttpMethod.Get, $"/api/v1/projects/{created!.Data!.Id}", Guid.NewGuid(), AuthSecurityConstants.Roles.Supervisor);
        var response = await Client.SendAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Summary", "2026", "Semester 1", "title")]
    [InlineData("Project", "", "2026", "Semester 1", "summary")]
    [InlineData("Project", "Summary", "", "Semester 1", "batch")]
    [InlineData("Project", "Summary", "2026", "", "semester")]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Missing_required_fields_are_identified(string title, string summary, string batch, string semester, string expectedField)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", Guid.NewGuid(), AuthSecurityConstants.Roles.Supervisor, new { title, summary, batch, semester });
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        Assert.Equal("VALIDATION_ERROR", payload?.Error?.Code);
        Assert.Contains(payload?.Error?.FieldErrors ?? [], error => error.Field == expectedField);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Oversized_title_is_rejected()
    {
        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v1/projects", Guid.NewGuid(), AuthSecurityConstants.Roles.Supervisor, new
        {
            title = new string('T', 41), summary = "Summary", batch = "2026", semester = "Semester 1"
        });
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(TestContext.Current.CancellationToken);
        Assert.Contains(payload?.Error?.FieldErrors ?? [], error => error.Field == "title");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    private static object ValidCreateRequest() => new
    {
        title = "AI Research Assistant",
        summary = "Research into reliable AI-assisted academic workflows.",
        batch = "2026",
        semester = "Semester 1"
    };

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, Guid userId, string role, object body)
    {
        var request = CreateRequest(method, path, userId, role);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, Guid userId, string role)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(userId, role));
        return request;
    }

    private static string CreateJwt(Guid userId, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(), ["role"] = role, ["iss"] = Issuer, ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(), ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds()
        };
        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{encodedHeader}.{encodedPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        var signature = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");
    private ResearchTrackWebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Test factory is not initialized.");
}
