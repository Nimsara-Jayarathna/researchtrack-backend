using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;
using ProjectApplicationService = ResearchTrack.ProjectService.Features.Projects.ProjectService;

namespace ResearchTrack.ProjectService.Tests.Services;

public sealed class ProjectServiceCoverageHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CanSupervisorManageAsync_ReturnsTrueOnlyForOwningSupervisor()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner);
        var sut = CreateSut(factory);

        Assert.True(await sut.CanSupervisorManageAsync(owner, project.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanSupervisorManageAsync(Guid.NewGuid(), project.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAccessAsync_RecognizesSupervisorAndStudentMembership_AndRejectsUnknownRole()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, student);
        var sut = CreateSut(factory);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await sut.CanAccessAsync(owner, AuthSecurityConstants.Roles.Supervisor, project.Id, ct));
        Assert.True(await sut.CanAccessAsync(student, AuthSecurityConstants.Roles.Student, project.Id, ct));
        Assert.False(await sut.CanAccessAsync(Guid.NewGuid(), AuthSecurityConstants.Roles.Student, project.Id, ct));
        Assert.False(await sut.CanAccessAsync(owner, "ADMIN", project.Id, ct));
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_FiltersProjectsByRoleAndMembership()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var owned = await SeedProjectAsync(factory, owner, student, "Owned Project");
        await SeedProjectAsync(factory, otherOwner, Guid.NewGuid(), "Other Project");
        var sut = CreateSut(factory);
        var ct = TestContext.Current.CancellationToken;

        var supervisorProjects = await sut.GetAccessibleProjectsAsync(owner, AuthSecurityConstants.Roles.Supervisor, ct);
        var studentProjects = await sut.GetAccessibleProjectsAsync(student, AuthSecurityConstants.Roles.Student, ct);
        var invalidRoleProjects = await sut.GetAccessibleProjectsAsync(owner, "ADMIN", ct);

        Assert.Equal(owned.Id, Assert.Single(supervisorProjects).Id);
        Assert.Equal(owned.Id, Assert.Single(studentProjects).Id);
        Assert.Empty(invalidRoleProjects);
    }

    [Fact]
    public async Task GetAccessibleProjectAsync_ReturnsFullGraphForMember_AndNullForUnauthorizedUser()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, student);
        var sut = CreateSut(factory);
        var ct = TestContext.Current.CancellationToken;

        var response = await sut.GetAccessibleProjectAsync(student, AuthSecurityConstants.Roles.Student, project.Id, ct);
        var denied = await sut.GetAccessibleProjectAsync(Guid.NewGuid(), AuthSecurityConstants.Roles.Student, project.Id, ct);
        var missing = await sut.GetAccessibleProjectAsync(owner, AuthSecurityConstants.Roles.Supervisor, Guid.NewGuid(), ct);

        Assert.NotNull(response);
        Assert.Equal(project.Id, response!.Id);
        Assert.Equal(2, response.Members.Count);
        Assert.Single(response.Milestones);
        Assert.Null(denied);
        Assert.Null(missing);
    }

    [Fact]
    public async Task UpdateAsync_ValidOwner_UpdatesCoreFieldsAndLifecycle()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner);
        var sut = CreateSut(factory);

        var result = await sut.UpdateAsync(
            owner,
            project.Id,
            new UpdateProjectRequest(" Updated ", " Updated summary ", "Y4.S1", ProjectSemesters.Semester2, "active"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Updated", result.Title);
        Assert.Equal("Updated summary", result.Summary);
        Assert.Equal(ProjectLifecycleStatuses.Active, result.LifecycleStatus);
        Assert.Equal(ProjectSemesters.Semester2, result.Semester);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_IsForbidden()
    {
        var factory = new TestProjectDbContextFactory();
        var project = await SeedProjectAsync(factory, Guid.NewGuid());
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsync(
            Guid.NewGuid(),
            project.Id,
            new UpdateProjectRequest("Title", "Summary", "Y3.S1", ProjectSemesters.Semester1, ProjectLifecycleStatuses.Active),
            TestContext.Current.CancellationToken));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateLeaderAsync_RequiresExistingStudentMember_AndCanSetValidLeader()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, student);
        var sut = CreateSut(factory);
        var ct = TestContext.Current.CancellationToken;

        var invalid = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateLeaderAsync(
            owner, project.Id, new UpdateProjectLeaderRequest(Guid.NewGuid()), ct));
        Assert.Equal(400, invalid.StatusCode);

        var updated = await sut.UpdateLeaderAsync(owner, project.Id, new UpdateProjectLeaderRequest(student), ct);
        Assert.Equal(student, updated.Leader?.Id);
    }

    [Fact]
    public async Task RemoveStudentAsync_RemovesMembershipAndClearsLeader()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, student);
        await using (var db = factory.CreateDbContext())
        {
            var persisted = await db.Projects.SingleAsync(x => x.Id == project.Id, TestContext.Current.CancellationToken);
            persisted.LeaderStudentUserId = student;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var sut = CreateSut(factory);

        var result = await sut.RemoveStudentAsync(owner, project.Id, student, TestContext.Current.CancellationToken);

        Assert.Null(result.Leader);
        Assert.DoesNotContain(result.Members, member => member.Id == student);
    }

    private static ProjectApplicationService CreateSut(TestProjectDbContextFactory factory) =>
        new(factory, Substitute.For<IAuthUserDirectoryClient>(), new FixedTimeProvider(Now));

    private static async Task<Project> SeedProjectAsync(
        TestProjectDbContextFactory factory,
        Guid supervisorId,
        Guid? studentId = null,
        string title = "ResearchTrack")
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = title,
            Summary = "Summary",
            Batch = "Y3.S1",
            Semester = ProjectSemesters.Semester1,
            LifecycleStatus = ProjectLifecycleStatuses.Planning,
            ProgressPercent = 25,
            SupervisorUserId = supervisorId,
            MilestoneDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(7),
            LastActivityAt = Now.UtcDateTime,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };

        await using var db = factory.CreateDbContext();
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = supervisorId,
            MemberRole = ProjectMemberRoles.Supervisor,
            FirstName = "Ada",
            LastName = "Supervisor",
            Email = "ada@example.edu",
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        });
        if (studentId is Guid sid)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                UserId = sid,
                MemberRole = ProjectMemberRoles.Student,
                FirstName = "Sam",
                LastName = "Student",
                Email = "sam@example.edu",
                RegistrationNumber = "IT001",
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            });
        }
        db.ProjectMilestones.Add(new ProjectMilestone
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Title = "M1",
            DueDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(7),
            Status = ProjectMilestoneStatuses.Planned,
            SequenceNo = 1,
            CreatedByUserId = supervisorId,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return project;
    }

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options =
            new DbContextOptionsBuilder<ProjectDbContext>()
                .UseInMemoryDatabase($"project-hardening-{Guid.NewGuid():N}")
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
