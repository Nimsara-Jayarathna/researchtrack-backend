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
        Assert.Contains(
            studentPayload?.Data?.Members ?? [],
            member =>
                member.Id == SupervisorA &&
                member.MemberRole == AuthSecurityConstants.Roles.Supervisor);
        Assert.Contains(
            studentPayload?.Data?.Members ?? [],
            member =>
                member.Id == StudentA &&
                member.MemberRole == AuthSecurityConstants.Roles.Student);
        Assert.Contains(
            studentPayload?.Data?.Members ?? [],
            member =>
                member.Id == StudentB &&
                member.MemberRole == AuthSecurityConstants.Roles.Student);
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

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Owning_supervisor_adds_registered_student_and_student_gains_access()
    {
        var projectId = await CreateProjectAsync();

        using var addRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/members",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { studentIds = new[] { StudentC } });

        using var addResponse = await Client.SendAsync(
            addRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var addPayload =
            await addResponse.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
                TestContext.Current.CancellationToken);
        Assert.Contains(
            addPayload?.Data?.Members ?? [],
            member =>
                member.Id == StudentC &&
                member.MemberRole == AuthSecurityConstants.Roles.Student);

        using var studentRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            StudentC,
            AuthSecurityConstants.Roles.Student);
        using var studentResponse = await Client.SendAsync(
            studentRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, studentResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Duplicate_project_membership_is_rejected_and_only_one_relationship_remains()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/members",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { studentIds = new[] { StudentA } });

        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var dbContext = await CreateDbContextAsync();
        var membershipCount = await dbContext.ProjectMembers.CountAsync(
            member =>
                member.ProjectId == projectId &&
                member.UserId == StudentA,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, membershipCount);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Removing_student_revokes_project_access()
    {
        var projectId = await CreateProjectAsync();

        using var removeRequest = CreateRequest(
            HttpMethod.Delete,
            $"/api/v1/projects/{projectId}/members/{StudentB}",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        using var removeResponse = await Client.SendAsync(
            removeRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var removePayload =
            await removeResponse.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
                TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            removePayload?.Data?.Members ?? [],
            member => member.Id == StudentB);

        using var studentRequest = CreateRequest(
            HttpMethod.Get,
            $"/api/v1/projects/{projectId}",
            StudentB,
            AuthSecurityConstants.Roles.Student);
        using var studentResponse = await Client.SendAsync(
            studentRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, studentResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_cannot_add_or_remove_project_members()
    {
        var projectId = await CreateProjectAsync();

        using var addRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/members",
            StudentA,
            AuthSecurityConstants.Roles.Student,
            new { studentIds = new[] { StudentC } });
        using var addResponse = await Client.SendAsync(
            addRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);

        using var removeRequest = CreateRequest(
            HttpMethod.Delete,
            $"/api/v1/projects/{projectId}/members/{StudentB}",
            StudentA,
            AuthSecurityConstants.Roles.Student);
        using var removeResponse = await Client.SendAsync(
            removeRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_cannot_manage_members_of_another_supervisors_project()
    {
        var projectId = await CreateProjectAsync();

        using var addRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/members",
            SupervisorB,
            AuthSecurityConstants.Roles.Supervisor,
            new { studentIds = new[] { StudentC } });
        using var addResponse = await Client.SendAsync(
            addRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, addResponse.StatusCode);

        using var removeRequest = CreateRequest(
            HttpMethod.Delete,
            $"/api/v1/projects/{projectId}/members/{StudentB}",
            SupervisorB,
            AuthSecurityConstants.Roles.Supervisor);
        using var removeResponse = await Client.SendAsync(
            removeRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Owning_supervisor_can_assign_an_active_project_student_as_leader()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/v1/projects/{projectId}/leader",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { leaderStudentId = StudentB });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload =
            await response.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
                TestContext.Current.CancellationToken);
        Assert.Equal(StudentB, payload?.Data?.Leader?.Id);

        await using var dbContext = await CreateDbContextAsync();
        var project = await dbContext.Projects.SingleAsync(
            item => item.Id == projectId,
            TestContext.Current.CancellationToken);
        Assert.Equal(StudentB, project.LeaderStudentUserId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Project_leader_must_be_an_active_student_member_of_that_project()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/v1/projects/{projectId}/leader",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { leaderStudentId = StudentC });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var dbContext = await CreateDbContextAsync();
        var project = await dbContext.Projects.SingleAsync(
            item => item.Id == projectId,
            TestContext.Current.CancellationToken);
        Assert.Equal(StudentA, project.LeaderStudentUserId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_cannot_update_project_leader()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/v1/projects/{projectId}/leader",
            StudentA,
            AuthSecurityConstants.Roles.Student,
            new { leaderStudentId = StudentB });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Supervisor_cannot_update_leader_of_another_supervisors_project()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/v1/projects/{projectId}/leader",
            SupervisorB,
            AuthSecurityConstants.Roles.Supervisor,
            new { leaderStudentId = StudentB });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Owning_supervisor_can_clear_project_leader()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/v1/projects/{projectId}/leader",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { leaderStudentId = (Guid?)null });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload =
            await response.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
                TestContext.Current.CancellationToken);
        Assert.Null(payload?.Data?.Leader);

        await using var dbContext = await CreateDbContextAsync();
        var project = await dbContext.Projects.SingleAsync(
            item => item.Id == projectId,
            TestContext.Current.CancellationToken);
        Assert.Null(project.LeaderStudentUserId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Removing_current_leader_clears_leader_reference()
    {
        var projectId = await CreateProjectAsync();

        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/api/v1/projects/{projectId}/members/{StudentA}",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload =
            await response.Content.ReadFromJsonAsync<ApiResponse<ProjectResponse>>(
                TestContext.Current.CancellationToken);
        Assert.Null(payload?.Data?.Leader);

        await using var dbContext = await CreateDbContextAsync();
        var project = await dbContext.Projects.SingleAsync(
            item => item.Id == projectId,
            TestContext.Current.CancellationToken);
        Assert.Null(project.LeaderStudentUserId);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Invalid_student_in_member_batch_rejects_entire_add_operation()
    {
        var projectId = await CreateProjectAsync();
        var invalidStudentId =
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/projects/{projectId}/members",
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor,
            new { studentIds = new[] { StudentC, invalidStudentId } });
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var dbContext = await CreateDbContextAsync();
        Assert.False(await dbContext.ProjectMembers.AnyAsync(
            member =>
                member.ProjectId == projectId &&
                member.UserId == StudentC,
            TestContext.Current.CancellationToken));
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
                [StudentB] = new(StudentB, "Bob", "Student", "bob@students.example.edu", "ST00000002", "STUDENT"),
                [StudentC] = new(StudentC, "Cara", "Student", "cara@students.example.edu", "ST00000003", "STUDENT")
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
