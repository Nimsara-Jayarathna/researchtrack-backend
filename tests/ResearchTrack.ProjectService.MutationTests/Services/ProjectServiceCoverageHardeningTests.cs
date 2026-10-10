using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;
using ProjectApplicationService = ResearchTrack.ProjectService.Features.Projects.ProjectService;

namespace ResearchTrack.ProjectService.MutationTests.Services;

public sealed class ProjectServiceCoverageHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("", "Summary", "Y3.S1", "ACTIVE")]
    [InlineData("Title", "", "Y3.S1", "ACTIVE")]
    [InlineData("Title", "Summary", "", "ACTIVE")]
    [InlineData("Title", "Summary", "Y3.S1", "BOGUS")]
    public async Task UpdateAsync_InvalidRequest_IsRejectedBeforeMutation(
        string title, string summary, string batch, string lifecycleStatus)
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        await Assert.ThrowsAnyAsync<ApiException>(() => sut.UpdateAsync(
            supervisorId,
            project.Id,
            new UpdateProjectRequest(title, summary, batch, ProjectSemesters.Semester1, lifecycleStatus),
            CancellationToken.None));

        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(CancellationToken.None);
        Assert.Equal("Original", persisted.Title);
    }

    [Fact]
    public async Task UpdateAsync_ValidOwner_NormalizesAndPersistsFields()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        var result = await sut.UpdateAsync(
            supervisorId,
            project.Id,
            new UpdateProjectRequest(" Updated ", " New summary ", " Y4.S1 ", ProjectSemesters.Semester2, "active"),
            CancellationToken.None);

        Assert.Equal("Updated", result.Title);
        Assert.Equal("New summary", result.Summary);
        Assert.Equal("Y4.S1", result.Batch);
        Assert.Equal(ProjectSemesters.Semester2, result.Semester);
        Assert.Equal(ProjectLifecycleStatuses.Active, result.LifecycleStatus);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_IsForbidden()
    {
        var factory = new TestProjectDbContextFactory();
        var project = await SeedProjectAsync(factory, Guid.NewGuid());
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsync(
            Guid.NewGuid(), project.Id,
            new UpdateProjectRequest("Updated", "Summary", "Y3.S1", ProjectSemesters.Semester1, null),
            CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_MissingProject_ReturnsNotFound()
    {
        var sut = CreateSut(new TestProjectDbContextFactory());
        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new UpdateProjectRequest("Updated", "Summary", "Y3.S1", ProjectSemesters.Semester1, null),
            CancellationToken.None));
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateLeaderAsync_StudentMember_SetsLeader()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId, studentId);
        var sut = CreateSut(factory);

        var result = await sut.UpdateLeaderAsync(supervisorId, project.Id,
            new UpdateProjectLeaderRequest(studentId), CancellationToken.None);

        Assert.Equal(studentId, result.Leader?.Id);
        await using var db = factory.CreateDbContext();
        Assert.Equal(studentId, (await db.Projects.SingleAsync(CancellationToken.None)).LeaderStudentUserId);
    }

    [Fact]
    public async Task UpdateLeaderAsync_NonMember_IsRejected()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateLeaderAsync(
            supervisorId, project.Id, new UpdateProjectLeaderRequest(Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateLeaderAsync_Null_ClearsExistingLeader()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId, studentId, studentId);
        var sut = CreateSut(factory);

        var result = await sut.UpdateLeaderAsync(supervisorId, project.Id,
            new UpdateProjectLeaderRequest(null), CancellationToken.None);

        Assert.Null(result.Leader);
    }

    [Fact]
    public async Task AddMembersAsync_ValidStudents_PersistsMembersAndResolvesOnce()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var studentA = Guid.NewGuid();
        var studentB = Guid.NewGuid();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None)
            .Returns([
                Student(studentA, "Ada"),
                Student(studentB, "Ben")
            ]);
        var sut = CreateSut(factory, directory);

        var result = await sut.AddMembersAsync(supervisorId, project.Id,
            new AddProjectMembersRequest { StudentIds = [studentA, studentB] },
            CancellationToken.None);

        Assert.Equal(2, result.Members.Count(x => x.MemberRole == ProjectMemberRoles.Student));
        await directory.Received(1).ResolveStudentsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            CancellationToken.None);
    }

    [Fact]
    public async Task AddMembersAsync_ExistingMember_ReturnsConflict()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId, studentId);
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), CancellationToken.None)
            .Returns([Student(studentId, "Sam")]);
        var sut = CreateSut(factory, directory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.AddMembersAsync(
            supervisorId, project.Id,
            new AddProjectMembersRequest { StudentIds = [studentId] },
            CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task RemoveStudentAsync_LeaderStudent_ClearsLeaderAndRemovesMembership()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId, studentId, studentId);
        var sut = CreateSut(factory);

        var result = await sut.RemoveStudentAsync(supervisorId, project.Id, studentId,
            CancellationToken.None);

        Assert.Null(result.Leader);
        Assert.DoesNotContain(result.Members, member => member.Id == studentId);
    }

    [Fact]
    public async Task RemoveStudentAsync_MissingMembership_ReturnsNotFound()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.RemoveStudentAsync(
            supervisorId, project.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task AddMilestoneAsync_ValidRequest_AppendsSequenceAndUpdatesAggregate()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        await SeedMilestoneAsync(factory, project.Id, supervisorId, 1, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(3));
        var sut = CreateSut(factory);

        var result = await sut.AddMilestoneAsync(supervisorId, project.Id,
            new CreateProjectMilestoneRequest
            {
                Title = " M2 ",
                Description = " second ",
                DueDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(8)
            }, CancellationToken.None);

        Assert.Equal(2, result.SequenceNo);
        Assert.Equal("M2", result.Title);
        await using var db = factory.CreateDbContext();
        Assert.Equal(2, await db.ProjectMilestones.CountAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AddMilestoneAsync_MissingRequiredFields_IsRejected(bool missingTitle, bool missingDate)
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.AddMilestoneAsync(
            supervisorId, project.Id,
            new CreateProjectMilestoneRequest
            {
                Title = missingTitle ? " " : "M2",
                DueDate = missingDate ? null : DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5)
            }, CancellationToken.None));
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateMilestoneAsync_PlannedToInProgress_PersistsStatus()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var milestone = await SeedMilestoneAsync(factory, project.Id, supervisorId, 1,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5));
        var sut = CreateSut(factory);

        var result = await sut.UpdateMilestoneAsync(supervisorId, project.Id, milestone.Id,
            new UpdateProjectMilestoneRequest("Updated milestone", null, milestone.DueDate, ProjectMilestoneStatuses.InProgress),
            CancellationToken.None);

        Assert.Equal(ProjectMilestoneStatuses.InProgress, result.Status);
        Assert.Equal("Updated milestone", result.Title);
    }

    [Fact]
    public async Task UpdateMilestoneAsync_MissingMilestone_ReturnsNotFound()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisorId = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, supervisorId);
        var sut = CreateSut(factory);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateMilestoneAsync(
            supervisorId, project.Id, Guid.NewGuid(),
            new UpdateProjectMilestoneRequest("M", null, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5), ProjectMilestoneStatuses.Planned),
            CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
    }

    private static ProjectApplicationService CreateSut(
        TestProjectDbContextFactory factory,
        IAuthUserDirectoryClient? directory = null) =>
        new(factory, directory ?? Substitute.For<IAuthUserDirectoryClient>(), new FixedTimeProvider(Now));

    private static AuthDirectoryUser Student(Guid id, string firstName) =>
        new(id, firstName, "Student", $"{firstName.ToLowerInvariant()}@example.com", $"IT{id.ToString("N")[..5]}", "STUDENT");

    private static async Task<Project> SeedProjectAsync(
        TestProjectDbContextFactory factory,
        Guid supervisorId,
        Guid? studentId = null,
        Guid? leaderId = null)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = "Original",
            Summary = "Original summary",
            Batch = "Y3.S1",
            Semester = ProjectSemesters.Semester1,
            LifecycleStatus = ProjectLifecycleStatuses.Planning,
            ProgressPercent = 0,
            SupervisorUserId = supervisorId,
            LeaderStudentUserId = leaderId,
            MilestoneDate = null,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
            LastActivityAt = Now.UtcDateTime
        };
        await using var db = factory.CreateDbContext();
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, UserId = supervisorId,
            MemberRole = ProjectMemberRoles.Supervisor, FirstName = "Ada", LastName = "Supervisor",
            Email = "ada@example.com", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
        });
        if (studentId.HasValue)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, UserId = studentId.Value,
                MemberRole = ProjectMemberRoles.Student, FirstName = "Sam", LastName = "Student",
                Email = "sam@example.com", RegistrationNumber = "IT001",
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
            });
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return project;
    }

    private static async Task<ProjectMilestone> SeedMilestoneAsync(
        TestProjectDbContextFactory factory,
        Guid projectId,
        Guid supervisorId,
        int sequence,
        DateOnly dueDate)
    {
        var milestone = new ProjectMilestone
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = $"M{sequence}",
            DueDate = dueDate, Status = ProjectMilestoneStatuses.Planned, SequenceNo = sequence,
            CreatedByUserId = supervisorId, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
        };
        await using var db = factory.CreateDbContext();
        db.ProjectMilestones.Add(milestone);
        await db.SaveChangesAsync(CancellationToken.None);
        return milestone;
    }

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options =
            new DbContextOptionsBuilder<ProjectDbContext>()
                .UseInMemoryDatabase($"project-coverage-hardening-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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
