using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;
using ResearchTrack.Testing;

namespace ResearchTrack.ProjectService.Tests.Integration;

public sealed class ProjectIntegrationTests : IAsyncLifetime
{
    private const string Issuer = "ResearchTrack.AuthService.Tests";
    private const string Audience = "ResearchTrack.Tests";
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long-123456789";

    private static readonly Guid SupervisorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SupervisorB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid StudentB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid StudentC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString = TestDatabaseConfiguration.GetRequiredConnectionString("PROJECT");
        _factory = new ResearchTrackWebApplicationFactory<Program>(
            connectionString,
            services =>
            {
                services.RemoveAll<IAuthUserDirectoryClient>();
                services.AddSingleton<IAuthUserDirectoryClient>(new TestAuthUserDirectoryClient());
            });
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_creates_full_project_aggregate_atomically()
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/projects",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            ValidCreateRequest());

        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CreateProjectResponse>>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload?.Data);
        Assert.Equal("PLANNING", payload!.Data!.LifecycleStatus);
        Assert.Equal(0, payload.Data.ProgressPercent);
        Assert.Equal(2, payload.Data.Students.Count);
        Assert.Equal(StudentA, payload.Data.Leader?.Id);
        Assert.Equal(2, payload.Data.Milestones.Count);
        Assert.Equal(new DateOnly(2027, 1, 10), payload.Data.MilestoneDate);

        await using var dbContext = await CreateDbContextAsync();
        var project = await dbContext.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SupervisorA, project.SupervisorUserId);
        Assert.Equal(StudentA, project.LeaderStudentUserId);
        Assert.Equal(new DateOnly(2027, 1, 10), project.MilestoneDate);
        Assert.Equal(3, await dbContext.ProjectMembers.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await dbContext.ProjectMilestones.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_cannot_create_project()
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/projects",
            StudentA,
            AuthSecurityConstants.Roles.Student,
            ValidCreateRequest());

        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var dbContext = await CreateDbContextAsync();
        Assert.Equal(0, await dbContext.Projects.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_and_assigned_student_see_same_project_through_same_collection_api()
    {
        var projectId = await CreateProjectAsync();

        var supervisorProjects = await GetProjectsAsync(
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        Assert.Contains(supervisorProjects, project => project.Id == projectId);

        var studentProjects = await GetProjectsAsync(
            StudentA,
            AuthSecurityConstants.Roles.Student);
        Assert.Contains(studentProjects, project => project.Id == projectId);
        Assert.Equal("Dr Supervisor", studentProjects.Single().SupervisorName);

        var unrelatedStudentProjects = await GetProjectsAsync(
            StudentC,
            AuthSecurityConstants.Roles.Student);
        Assert.Empty(unrelatedStudentProjects);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Project_detail_is_available_to_owner_and_assigned_student_only()
    {
        var projectId = await CreateProjectAsync();

        using var ownerRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        var ownerResponse = await Client.SendAsync(ownerRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        using var studentRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            StudentB,
            AuthSecurityConstants.Roles.Student);
        var studentResponse = await Client.SendAsync(studentRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, studentResponse.StatusCode);
        var studentPayload = await studentResponse.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(3, studentPayload?.Data?.Members.Count);
        Assert.Equal(2, studentPayload?.Data?.Milestones.Count);
        Assert.Equal(StudentA, studentPayload?.Data?.Leader?.Id);

        using var unrelatedStudentRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            StudentC,
            AuthSecurityConstants.Roles.Student);
        var unrelatedStudentResponse = await Client.SendAsync(
            unrelatedStudentRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedStudentResponse.StatusCode);

        using var otherSupervisorRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            SupervisorB,
            AuthSecurityConstants.Roles.Supervisor);
        var otherSupervisorResponse = await Client.SendAsync(
            otherSupervisorRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, otherSupervisorResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Invalid_leader_is_rejected_before_persistence()
    {
        var body = new
        {
            title = "AI Research Assistant",
            summary = "Research into reliable AI-assisted academic workflows.",
            batch = "2026",
            semester = "Semester 1",
            studentIds = new[] { StudentA, StudentB },
            leaderStudentId = StudentC,
            milestones = ValidMilestones()
        };

        var response = await SendCreateAsync(body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Contains(
            payload?.Error?.FieldErrors ?? [],
            error => error.Field == "leaderStudentId");
        await AssertDatabaseEmptyAsync();
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Invalid_or_non_student_ids_are_rejected_before_persistence()
    {
        var invalidStudentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var body = new
        {
            title = "AI Research Assistant",
            summary = "Research into reliable AI-assisted academic workflows.",
            batch = "2026",
            semester = "Semester 1",
            studentIds = new[] { StudentA, invalidStudentId },
            leaderStudentId = StudentA,
            milestones = ValidMilestones()
        };

        var response = await SendCreateAsync(body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Contains(payload?.Error?.FieldErrors ?? [], error => error.Field == "studentIds");
        await AssertDatabaseEmptyAsync();
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Milestones_must_be_future_and_chronological()
    {
        var body = new
        {
            title = "AI Research Assistant",
            summary = "Research into reliable AI-assisted academic workflows.",
            batch = "2026",
            semester = "Semester 1",
            studentIds = new[] { StudentA },
            leaderStudentId = StudentA,
            milestones = new object[]
            {
                new { title = "Second", description = "", dueDate = "2027-02-10" },
                new { title = "First", description = "", dueDate = "2027-01-10" }
            }
        };

        var response = await SendCreateAsync(body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(
            TestContext.Current.CancellationToken);
        Assert.Contains(
            payload?.Error?.FieldErrors ?? [],
            error => error.Field == "milestones[1].dueDate");
        await AssertDatabaseEmptyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private async Task<Guid> CreateProjectAsync()
    {
        using var response = await SendCreateAsync(ValidCreateRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<CreateProjectResponse>>(
            TestContext.Current.CancellationToken);
        return payload?.Data?.Id ?? throw new InvalidOperationException("Project was not created.");
    }

    private async Task<IReadOnlyList<ProjectSummaryResponse>> GetProjectsAsync(Guid userId, string role)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/projects", userId, role);
        using var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<ProjectSummaryResponse>>>(
            TestContext.Current.CancellationToken);
        return payload?.Data ?? [];
    }

    private async Task<HttpResponseMessage> SendCreateAsync(object body)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/projects",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            body);
        return await Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static object ValidCreateRequest() => new
    {
        title = "AI Research Assistant",
        summary = "Research into reliable AI-assisted academic workflows.",
        batch = "2026",
        semester = "Semester 1",
        studentIds = new[] { StudentA, StudentB },
        leaderStudentId = StudentA,
        milestones = ValidMilestones()
    };

    private static object[] ValidMilestones() =>
    [
        new { title = "Proposal", description = "Initial proposal", dueDate = "2027-01-10" },
        new { title = "Prototype", description = "Initial prototype", dueDate = "2027-02-10" }
    ];

    private async Task AssertDatabaseEmptyAsync()
    {
        await using var dbContext = await CreateDbContextAsync();
        Assert.Equal(0, await dbContext.Projects.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await dbContext.ProjectMembers.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await dbContext.ProjectMilestones.CountAsync(TestContext.Current.CancellationToken));
    }

    private Task<ProjectDbContext> CreateDbContextAsync()
    {
        var dbFactory = Factory.Services
            .GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        return dbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string path,
        Guid userId,
        string role,
        object body)
    {
        var request = CreateRequest(method, path, userId, role);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        Guid userId,
        string role)
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
            ["sub"] = userId.ToString(),
            ["role"] = role,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds()
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
    private ResearchTrackWebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    private sealed class TestAuthUserDirectoryClient : IAuthUserDirectoryClient
    {
        private static readonly IReadOnlyDictionary<Guid, AuthDirectoryUser> Students =
            new Dictionary<Guid, AuthDirectoryUser>
            {
                [StudentA] = new(StudentA, "Alice", "Student", "alice@students.example.edu", "ST00000001", "STUDENT"),
                [StudentB] = new(StudentB, "Bob", "Student", "bob@students.example.edu", "ST00000002", "STUDENT")
            };

        public Task<AuthDirectoryUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AuthDirectoryUser(
                SupervisorA,
                "Dr",
                "Supervisor",
                "supervisor@staff.example.edu",
                null,
                "SUPERVISOR"));

        public Task<IReadOnlyList<AuthDirectoryUser>> ResolveStudentsAsync(
            IReadOnlyCollection<Guid> studentIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<AuthDirectoryUser> result = studentIds
                .Distinct()
                .Where(Students.ContainsKey)
                .Select(id => Students[id])
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
