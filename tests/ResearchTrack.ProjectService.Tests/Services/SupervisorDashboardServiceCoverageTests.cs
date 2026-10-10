using Microsoft.EntityFrameworkCore;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Features.Dashboard;
using ResearchTrack.ProjectService.Persistence;

namespace ResearchTrack.ProjectService.Tests.Services;

public sealed class SupervisorDashboardServiceCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_EmptySupervisor_ReturnsZeroAggregates()
    {
        var factory = new TestProjectDbContextFactory();
        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));

        var result = await sut.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.TotalProjects);
        Assert.Empty(result.Projects);
        Assert.Empty(result.RecentProjects);
        Assert.Equal(0, result.UpcomingMilestonesCount);
    }

    [Fact]
    public async Task GetAsync_AggregatesStatusesUpcomingMilestonesMembersAndRecentLimit()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisor = Guid.NewGuid();
        var statuses = new[]
        {
            ProjectLifecycleStatuses.Planning,
            ProjectLifecycleStatuses.Active,
            ProjectLifecycleStatuses.AtRisk,
            ProjectLifecycleStatuses.Behind,
            ProjectLifecycleStatuses.Completed,
            ProjectLifecycleStatuses.Active
        };

        await using (var db = factory.CreateDbContext())
        {
            for (var index = 0; index < statuses.Length; index++)
            {
                var projectId = Guid.NewGuid();
                db.Projects.Add(new Project
                {
                    Id = projectId,
                    Title = $"Project {index}",
                    Summary = "Summary",
                    Batch = "Y3.S1",
                    Semester = ProjectSemesters.Semester1,
                    LifecycleStatus = statuses[index],
                    ProgressPercent = index * 10,
                    SupervisorUserId = supervisor,
                    MilestoneDate = index < 2
                        ? DateOnly.FromDateTime(Now.UtcDateTime).AddDays(index + 1)
                        : DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30),
                    LastActivityAt = Now.UtcDateTime.AddHours(-index),
                    CreatedAt = Now.UtcDateTime.AddDays(-index),
                    UpdatedAt = Now.UtcDateTime
                });
                db.ProjectMembers.Add(new ProjectMember
                {
                    Id = Guid.NewGuid(), ProjectId = projectId, UserId = supervisor,
                    MemberRole = ProjectMemberRoles.Supervisor, FirstName = "Ada", LastName = "Supervisor",
                    Email = "ada@example.edu", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
                });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));
        var result = await sut.GetAsync(supervisor, TestContext.Current.CancellationToken);

        Assert.Equal(6, result.TotalProjects);
        Assert.Equal(1, result.PlanningProjects);
        Assert.Equal(2, result.ActiveProjects);
        Assert.Equal(1, result.AtRiskProjects);
        Assert.Equal(1, result.BehindProjects);
        Assert.Equal(1, result.CompletedProjects);
        Assert.Equal(2, result.UpcomingMilestonesCount);
        Assert.Equal(5, result.RecentProjects.Count);
        Assert.All(result.Projects, item => Assert.Equal(1, item.MemberCount));
        Assert.All(result.Projects, item => Assert.Equal("UNAVAILABLE", item.JiraHealthIndicator));
    }

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options =
            new DbContextOptionsBuilder<ProjectDbContext>()
                .UseInMemoryDatabase($"dashboard-hardening-{Guid.NewGuid():N}")
                .Options;
        public ProjectDbContext CreateDbContext() => new(_options);
        public Task<ProjectDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
