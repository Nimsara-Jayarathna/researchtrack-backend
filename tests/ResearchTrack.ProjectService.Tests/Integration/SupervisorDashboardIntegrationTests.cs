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
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Persistence;
using ResearchTrack.Testing;

namespace ResearchTrack.ProjectService.Tests.Integration;

public sealed class SupervisorDashboardIntegrationTests : IAsyncLifetime
{
    private const string Issuer = "ResearchTrack.AuthService.Tests";
    private const string Audience = "ResearchTrack.Tests";
    private const string SigningKey = "test-signing-key-that-is-at-least-32-bytes-long-123456789";

    private static readonly Guid SupervisorA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SupervisorB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var connectionString =
            TestDatabaseConfiguration.GetRequiredConnectionString("PROJECT");

        _factory = new ResearchTrackWebApplicationFactory<Program>(
            connectionString,
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(
                    new FixedTimeProvider(FixedNow));
            });
        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        await using var dbContext = await dbFactory.CreateDbContextAsync(
            TestContext.Current.CancellationToken);
        await dbContext.Database.EnsureDeletedAsync(
            TestContext.Current.CancellationToken);
        await dbContext.Database.MigrateAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Dashboard_returns_only_owned_projects_and_backend_aggregates()
    {
        var today = DateOnly.FromDateTime(FixedNow.UtcDateTime);

        var activeProjectId = await SeedProjectAsync(
            SupervisorA,
            "Active research",
            ProjectLifecycleStatuses.Active,
            today.AddDays(7),
            FixedNow.UtcDateTime.AddHours(-1),
            studentCount: 2);
        await SeedProjectAsync(
            SupervisorA,
            "At risk research",
            ProjectLifecycleStatuses.AtRisk,
            today.AddDays(20),
            FixedNow.UtcDateTime.AddHours(-2),
            studentCount: 1);
        await SeedProjectAsync(
            SupervisorA,
            "Completed research",
            ProjectLifecycleStatuses.Completed,
            today.AddDays(5),
            FixedNow.UtcDateTime.AddHours(-3),
            studentCount: 1);
        await SeedProjectAsync(
            SupervisorB,
            "Other supervisor project",
            ProjectLifecycleStatuses.Behind,
            today.AddDays(2),
            FixedNow.UtcDateTime,
            studentCount: 3);

        using var request = CreateRequest(
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ApiResponse<SupervisorDashboardResponse>>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(payload?.Data);
        var dashboard = payload!.Data!;

        Assert.Equal(3, dashboard.TotalProjects);
        Assert.Equal(0, dashboard.PlanningProjects);
        Assert.Equal(1, dashboard.ActiveProjects);
        Assert.Equal(1, dashboard.AtRiskProjects);
        Assert.Equal(0, dashboard.BehindProjects);
        Assert.Equal(1, dashboard.CompletedProjects);
        Assert.Equal(1, dashboard.UpcomingMilestonesCount);
        Assert.Equal(0, dashboard.JiraAtRiskCount);
        Assert.Equal(0, dashboard.JiraBehindCount);
        Assert.Equal(3, dashboard.Projects.Count);
        Assert.DoesNotContain(
            dashboard.Projects,
            project => project.Title == "Other supervisor project");

        var activeProject = dashboard.Projects.Single(
            project => project.Id == activeProjectId);
        Assert.Equal(3, activeProject.MemberCount);
        Assert.Equal("NOT_CONNECTED", activeProject.JiraHealthIndicator);
        Assert.Equal(activeProjectId, dashboard.RecentProjects[0].Id);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Dashboard_returns_zeroed_read_model_when_supervisor_has_no_projects()
    {
        using var request = CreateRequest(
            SupervisorA,
            AuthSecurityConstants.Roles.Supervisor);
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ApiResponse<SupervisorDashboardResponse>>(
                TestContext.Current.CancellationToken);
        Assert.NotNull(payload?.Data);
        var dashboard = payload!.Data!;

        Assert.Equal(0, dashboard.TotalProjects);
        Assert.Equal(0, dashboard.UpcomingMilestonesCount);
        Assert.Empty(dashboard.Projects);
        Assert.Empty(dashboard.RecentProjects);
    }

    [Fact]
    [Trait("Category", "DatabaseIntegration")]
    public async Task Student_cannot_open_supervisor_dashboard()
    {
        using var request = CreateRequest(
            Guid.NewGuid(),
            AuthSecurityConstants.Roles.Student);
        using var response = await Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private async Task<Guid> SeedProjectAsync(
        Guid supervisorUserId,
        string title,
        string lifecycleStatus,
        DateOnly? milestoneDate,
        DateTime lastActivityAt,
        int studentCount)
    {
        await using var dbContext = await CreateDbContextAsync();
        var projectId = Guid.NewGuid();
        var createdAt = lastActivityAt.AddDays(-1);

        dbContext.Projects.Add(new Project
        {
            Id = projectId,
            Title = title,
            Summary = $"Summary for {title}",
            Batch = "2026",
            Semester = "Semester 1",
            LifecycleStatus = lifecycleStatus,
            ProgressPercent = lifecycleStatus == ProjectLifecycleStatuses.Completed
                ? 100
                : 40,
            SupervisorUserId = supervisorUserId,
            LeaderStudentUserId = null,
            MilestoneDate = milestoneDate,
            LastActivityAt = lastActivityAt,
            CreatedAt = createdAt,
            UpdatedAt = lastActivityAt
        });

        dbContext.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UserId = supervisorUserId,
            MemberRole = ProjectMemberRoles.Supervisor,
            FirstName = "Dr",
            LastName = "Supervisor",
            Email = $"{supervisorUserId:N}@staff.example.edu",
            RegistrationNumber = null,
            CreatedAt = createdAt,
            UpdatedAt = lastActivityAt
        });

        for (var index = 0; index < studentCount; index++)
        {
            dbContext.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = Guid.NewGuid(),
                MemberRole = ProjectMemberRoles.Student,
                FirstName = $"Student{index + 1}",
                LastName = "Member",
                Email = $"student-{projectId:N}-{index}@students.example.edu",
                RegistrationNumber = $"ST{index + 1:00000000}",
                CreatedAt = createdAt,
                UpdatedAt = lastActivityAt
            });
        }

        await dbContext.SaveChangesAsync(
            TestContext.Current.CancellationToken);
        return projectId;
    }

    private Task<ProjectDbContext> CreateDbContextAsync()
    {
        var dbFactory = Factory.Services
            .GetRequiredService<IDbContextFactory<ProjectDbContext>>();
        return dbFactory.CreateDbContextAsync(
            TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        Guid userId,
        string role)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/supervisor/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(userId, role));
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

        var encodedHeader = Base64Url(
            JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(
            JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{encodedHeader}.{encodedPayload}";
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(SigningKey));
        var signature = Base64Url(
            hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private HttpClient Client =>
        _client ??
        throw new InvalidOperationException(
            "Test client is not initialized.");

    private ResearchTrackWebApplicationFactory<Program> Factory =>
        _factory ??
        throw new InvalidOperationException(
            "Test factory is not initialized.");

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
