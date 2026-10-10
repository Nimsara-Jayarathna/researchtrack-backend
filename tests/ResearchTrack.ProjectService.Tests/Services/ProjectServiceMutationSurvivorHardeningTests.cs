using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Features.Dashboard;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;
using ProjectApplicationService = ResearchTrack.ProjectService.Features.Projects.ProjectService;

namespace ResearchTrack.ProjectService.Tests.Services;

/// <summary>
/// Mutation-oriented behavioral tests for ProjectService orchestration and dashboard aggregation.
/// These tests intentionally assert independent authorization predicates, persistence side effects,
/// ordering, role boundaries, state transitions and aggregate updates rather than implementation details.
/// </summary>
public sealed class ProjectServiceMutationSurvivorHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task CreateAsync_RequiresMatchingSupervisorIdentityAndSupervisorRoleIndependently()
    {
        var student = Guid.NewGuid();
        var requestedSupervisor = Guid.NewGuid();

        var identityMismatchFactory = new TestProjectDbContextFactory();
        var identityMismatchDirectory = Substitute.For<IAuthUserDirectoryClient>();
        identityMismatchDirectory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(Guid.NewGuid(), "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Supervisor));
        var identityMismatchSut = CreateSut(identityMismatchFactory, identityMismatchDirectory);

        var identityMismatch = await Assert.ThrowsAsync<ApiException>(() => identityMismatchSut.CreateAsync(
            requestedSupervisor, ValidCreateRequest(student), TestContext.Current.CancellationToken));
        Assert.Equal(403, identityMismatch.StatusCode);
        await identityMismatchDirectory.DidNotReceive().ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());

        var roleMismatchFactory = new TestProjectDbContextFactory();
        var roleMismatchDirectory = Substitute.For<IAuthUserDirectoryClient>();
        roleMismatchDirectory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(requestedSupervisor, "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Student));
        var roleMismatchSut = CreateSut(roleMismatchFactory, roleMismatchDirectory);

        var roleMismatch = await Assert.ThrowsAsync<ApiException>(() => roleMismatchSut.CreateAsync(
            requestedSupervisor, ValidCreateRequest(student), TestContext.Current.CancellationToken));
        Assert.Equal(403, roleMismatch.StatusCode);
        await roleMismatchDirectory.DidNotReceive().ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_PersistsCompleteNormalizedGraphAndPreservesRequestedStudentOrder()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisor = Guid.NewGuid();
        var studentA = Guid.NewGuid();
        var studentB = Guid.NewGuid();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(supervisor, "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Supervisor));
        // Deliberately return reverse order; service must restore request order.
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AuthDirectoryUser(studentB, "Ben", "Beta", "ben@example.com", "IT002", AuthSecurityConstants.Roles.Student),
            new AuthDirectoryUser(studentA, "Ann", "Alpha", "ann@example.com", "IT001", AuthSecurityConstants.Roles.Student)
        });
        var sut = CreateSut(factory, directory);
        var request = new CreateProjectRequest
        {
            Title = "  Project X  ", Summary = "  Summary X  ", Batch = "  Y4.S1  ", Semester = ProjectSemesters.Semester2,
            StudentIds = new[] { studentA, studentB }, LeaderStudentId = studentB,
            Milestones = new[]
            {
                new CreateProjectMilestoneRequest { Title = "  M1  ", Description = "  First  ", DueDate = Today.AddDays(3) },
                new CreateProjectMilestoneRequest { Title = "M2", Description = "   ", DueDate = Today.AddDays(7) }
            }
        };

        var result = await sut.CreateAsync(supervisor, request, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Project X", result.Title);
        Assert.Equal("Summary X", result.Summary);
        Assert.Equal("Y4.S1", result.Batch);
        Assert.Equal(ProjectSemesters.Semester2, result.Semester);
        Assert.Equal(ProjectLifecycleStatuses.Planning, result.LifecycleStatus);
        Assert.Equal(0, result.ProgressPercent);
        Assert.Equal(Today.AddDays(3), result.MilestoneDate);
        Assert.Equal(new[] { studentA, studentB }, result.Students.Select(s => s.Id).ToArray());
        Assert.Equal(studentB, result.Leader!.Id);
        Assert.Equal(2, result.Milestones.Count);
        Assert.Equal(1, result.Milestones[0].SequenceNo);
        Assert.Equal(2, result.Milestones[1].SequenceNo);
        Assert.Equal(ProjectMilestoneStatuses.Planned, result.Milestones[0].Status);
        Assert.Equal("M1", result.Milestones[0].Title);
        Assert.Equal("First", result.Milestones[0].Description);
        Assert.Null(result.Milestones[1].Description);

        await using var db = factory.CreateDbContext();
        var project = await db.Projects.SingleAsync(p => p.Id == result.Id, TestContext.Current.CancellationToken);
        Assert.Equal(supervisor, project.SupervisorUserId);
        Assert.Equal(studentB, project.LeaderStudentUserId);
        Assert.Equal(Now.UtcDateTime, project.CreatedAt);
        Assert.Equal(Now.UtcDateTime, project.UpdatedAt);
        Assert.Equal(Now.UtcDateTime, project.LastActivityAt);
        Assert.Equal(3, await db.ProjectMembers.CountAsync(m => m.ProjectId == result.Id, TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.ProjectMilestones.CountAsync(m => m.ProjectId == result.Id, TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == result.Id && m.MemberRole == ProjectMemberRoles.Supervisor, TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.ProjectMembers.CountAsync(m => m.ProjectId == result.Id && m.MemberRole == ProjectMemberRoles.Student, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_UnresolvedStudentDoesNotPersistAnyAggregateRows()
    {
        var factory = new TestProjectDbContextFactory();
        var supervisor = Guid.NewGuid();
        var student = Guid.NewGuid();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(supervisor, "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Supervisor));
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<AuthDirectoryUser>());
        var sut = CreateSut(factory, directory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(supervisor, ValidCreateRequest(student), TestContext.Current.CancellationToken));

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.Projects.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ProjectMembers.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ProjectMilestones.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanSupervisorManageAsync_RequiresBothProjectAndOwningSupervisor()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var otherSupervisor = Guid.NewGuid();
        var owned = await SeedProjectAsync(factory, owner, "Owned");
        var other = await SeedProjectAsync(factory, otherSupervisor, "Other");
        var sut = CreateSut(factory);

        Assert.True(await sut.CanSupervisorManageAsync(owner, owned.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanSupervisorManageAsync(otherSupervisor, owned.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanSupervisorManageAsync(owner, other.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanSupervisorManageAsync(owner, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAccessAsync_SeparatesSupervisorStudentAndUnknownRoleRules()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: student);
        var sut = CreateSut(factory);

        Assert.True(await sut.CanAccessAsync(owner, "supervisor", project.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanAccessAsync(outsider, "SUPERVISOR", project.Id, TestContext.Current.CancellationToken));
        Assert.True(await sut.CanAccessAsync(student, "student", project.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanAccessAsync(owner, "STUDENT", project.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanAccessAsync(student, "ADMIN", project.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanAccessAsync(student, string.Empty, project.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAccessAsync_StudentMustHaveStudentMembershipInSameProject()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var p1 = await SeedProjectAsync(factory, owner, "P1", studentId: student);
        var p2 = await SeedProjectAsync(factory, owner, "P2");
        await AddMemberAsync(factory, p2.Id, student, ProjectMemberRoles.Supervisor, "Wrong", "Role");
        var sut = CreateSut(factory);

        Assert.True(await sut.CanAccessAsync(student, "STUDENT", p1.Id, TestContext.Current.CancellationToken));
        Assert.False(await sut.CanAccessAsync(student, "STUDENT", p2.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_SupervisorFiltersOrdersAndMapsAggregateFields()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var older = await SeedProjectAsync(factory, owner, "Older", createdAt: Now.UtcDateTime.AddDays(-2));
        var newer = await SeedProjectAsync(factory, owner, "Newer", createdAt: Now.UtcDateTime.AddDays(-1));
        _ = await SeedProjectAsync(factory, other, "Hidden", createdAt: Now.UtcDateTime);
        await AddMemberAsync(factory, newer.Id, Guid.NewGuid(), ProjectMemberRoles.Student, "Sue", "Student", "sue@example.com", "IT200");
        var sut = CreateSut(factory);

        var result = await sut.GetAccessibleProjectsAsync(owner, "supervisor", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(older.Id, result[1].Id);
        Assert.Equal(2, result[0].MemberCount);
        Assert.Equal("Ada Supervisor", result[0].SupervisorName);
        Assert.Equal("Newer", result[0].Title);
        Assert.Equal("Summary-Newer", result[0].Summary);
        Assert.Equal(ProjectLifecycleStatuses.Planning, result[0].LifecycleStatus);
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_StudentReturnsOnlySameProjectStudentMemberships()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var visible = await SeedProjectAsync(factory, owner, "Visible", studentId: student);
        var wrongRole = await SeedProjectAsync(factory, owner, "WrongRole");
        await AddMemberAsync(factory, wrongRole.Id, student, ProjectMemberRoles.Supervisor, "Student", "WrongRole");
        _ = await SeedProjectAsync(factory, owner, "NoMembership");
        var sut = CreateSut(factory);

        var result = await sut.GetAccessibleProjectsAsync(student, "StUdEnT", TestContext.Current.CancellationToken);

        var only = Assert.Single(result);
        Assert.Equal(visible.Id, only.Id);
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_UnknownRoleReturnsEmptyEvenWhenMembershipExists()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var user = Guid.NewGuid();
        _ = await SeedProjectAsync(factory, owner, "P", studentId: user);
        var sut = CreateSut(factory);

        Assert.Empty(await sut.GetAccessibleProjectsAsync(user, "ADMIN", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAccessibleProjectAsync_RequiresCorrectRoleSpecificAccessAndProjectIdentity()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: student);
        var sut = CreateSut(factory);

        Assert.NotNull(await sut.GetAccessibleProjectAsync(owner, "SUPERVISOR", project.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await sut.GetAccessibleProjectAsync(student, "student", project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetAccessibleProjectAsync(outsider, "SUPERVISOR", project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetAccessibleProjectAsync(outsider, "STUDENT", project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetAccessibleProjectAsync(owner, "ADMIN", project.Id, TestContext.Current.CancellationToken));
        Assert.Null(await sut.GetAccessibleProjectAsync(owner, "SUPERVISOR", Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAccessibleProjectAsync_MapsSupervisorLeaderMembersAndMilestonesInOrder()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: student, leaderId: student);
        var later = await SeedMilestoneAsync(factory, project.Id, owner, 2, Today.AddDays(8), "Second");
        var earlier = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(4), "First");
        var sut = CreateSut(factory);

        var result = await sut.GetAccessibleProjectAsync(owner, "SUPERVISOR", project.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(owner, result!.Supervisor!.Id);
        Assert.Equal(student, result.Leader!.Id);
        Assert.Equal(2, result.Members.Count);
        Assert.Equal(2, result.Milestones.Count);
        Assert.Equal(earlier.Id, result.Milestones[0].Id);
        Assert.Equal(later.Id, result.Milestones[1].Id);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_TrimsNormalizesUpdatesTimestampsAndPersists()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "Original", createdAt: Now.UtcDateTime.AddDays(-4), updatedAt: Now.UtcDateTime.AddDays(-3));
        var sut = CreateSut(factory);

        var result = await sut.UpdateAsync(owner, project.Id,
            new UpdateProjectRequest("  Updated  ", "  New summary  ", "  Y4.S2  ", ProjectSemesters.Semester2, " completed "),
            TestContext.Current.CancellationToken);

        Assert.Equal("Updated", result.Title);
        Assert.Equal("New summary", result.Summary);
        Assert.Equal("Y4.S2", result.Batch);
        Assert.Equal(ProjectSemesters.Semester2, result.Semester);
        Assert.Equal(ProjectLifecycleStatuses.Completed, result.LifecycleStatus);
        Assert.Equal(Now.UtcDateTime, result.LastActivityAt);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Now.UtcDateTime, persisted.UpdatedAt);
        Assert.Equal(Now.UtcDateTime, persisted.LastActivityAt);
    }

    [Fact]
    public async Task UpdateAsync_NullLifecyclePreservesExistingStatus()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", lifecycleStatus: ProjectLifecycleStatuses.AtRisk);
        var sut = CreateSut(factory);

        var result = await sut.UpdateAsync(owner, project.Id,
            new UpdateProjectRequest("P2", "S2", "Y4", ProjectSemesters.Semester1, null), TestContext.Current.CancellationToken);

        Assert.Equal(ProjectLifecycleStatuses.AtRisk, result.LifecycleStatus);
    }

    [Theory]
    [InlineData("PLANNING")]
    [InlineData("ACTIVE")]
    [InlineData("AT_RISK")]
    [InlineData("BEHIND")]
    [InlineData("COMPLETED")]
    public async Task UpdateAsync_AcceptsEverySupportedLifecycleStatus(string status)
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var sut = CreateSut(factory);

        var result = await sut.UpdateAsync(owner, project.Id,
            new UpdateProjectRequest("P", "S", "B", ProjectSemesters.Semester1, status.ToLowerInvariant()), TestContext.Current.CancellationToken);

        Assert.Equal(status, result.LifecycleStatus);
    }

    [Fact]
    public async Task UpdateAsync_NonOwnerDoesNotMutatePersistedProject()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "Original");
        var sut = CreateSut(factory);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsync(Guid.NewGuid(), project.Id,
            new UpdateProjectRequest("Changed", "Changed", "B", ProjectSemesters.Semester2, ProjectLifecycleStatuses.Active), TestContext.Current.CancellationToken));

        Assert.Equal(403, ex.StatusCode);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Original", persisted.Title);
        Assert.Equal(ProjectLifecycleStatuses.Planning, persisted.LifecycleStatus);
    }

    [Fact]
    public async Task UpdateLeaderAsync_RejectsStudentFromDifferentProjectOrWrongMembershipRole()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "Target");
        var other = await SeedProjectAsync(factory, owner, "Other", studentId: student);
        var wrongRole = Guid.NewGuid();
        await AddMemberAsync(factory, project.Id, wrongRole, ProjectMemberRoles.Supervisor, "Wrong", "Role");
        var sut = CreateSut(factory);

        var differentProject = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateLeaderAsync(owner, project.Id, new UpdateProjectLeaderRequest(student), TestContext.Current.CancellationToken));
        var wrongMembership = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateLeaderAsync(owner, project.Id, new UpdateProjectLeaderRequest(wrongRole), TestContext.Current.CancellationToken));

        Assert.Equal(400, differentProject.StatusCode);
        Assert.Equal(400, wrongMembership.StatusCode);
        Assert.NotEqual(project.Id, other.Id);
    }

    [Fact]
    public async Task UpdateLeaderAsync_SetAndClearLeaderUpdatesPersistenceAndTimestamps()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: student);
        var sut = CreateSut(factory);

        var set = await sut.UpdateLeaderAsync(owner, project.Id, new UpdateProjectLeaderRequest(student), TestContext.Current.CancellationToken);
        Assert.Equal(student, set.Leader!.Id);
        var cleared = await sut.UpdateLeaderAsync(owner, project.Id, new UpdateProjectLeaderRequest(null), TestContext.Current.CancellationToken);
        Assert.Null(cleared.Leader);

        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Null(persisted.LeaderStudentUserId);
        Assert.Equal(Now.UtcDateTime, persisted.UpdatedAt);
        Assert.Equal(Now.UtcDateTime, persisted.LastActivityAt);
    }

    [Fact]
    public async Task AddMembersAsync_NonOwnerStopsBeforeDirectoryResolution()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var sut = CreateSut(factory, directory);
        var student = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.AddMembersAsync(Guid.NewGuid(), project.Id,
            new AddProjectMembersRequest { StudentIds = new[] { student } }, TestContext.Current.CancellationToken));

        Assert.Equal(403, ex.StatusCode);
        await directory.DidNotReceive().ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddMembersAsync_MissingProjectStopsBeforeDirectoryResolution()
    {
        var factory = new TestProjectDbContextFactory();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var sut = CreateSut(factory, directory);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.AddMembersAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AddProjectMembersRequest { StudentIds = new[] { Guid.NewGuid() } }, TestContext.Current.CancellationToken));

        Assert.Equal(404, ex.StatusCode);
        await directory.DidNotReceive().ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddMembersAsync_UnresolvedStudentDoesNotPersistPartialMembership()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var requestedA = Guid.NewGuid();
        var requestedB = Guid.NewGuid();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Student(requestedA, "A") });
        var sut = CreateSut(factory, directory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.AddMembersAsync(owner, project.Id,
            new AddProjectMembersRequest { StudentIds = new[] { requestedA, requestedB } }, TestContext.Current.CancellationToken));

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.ProjectMembers.CountAsync(m => m.ProjectId == project.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMembersAsync_ExistingMemberConflictDoesNotAddAnyOfRequestedBatch()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var existing = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: existing);
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Student(existing, "Existing"), Student(fresh, "Fresh") });
        var sut = CreateSut(factory, directory);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.AddMembersAsync(owner, project.Id,
            new AddProjectMembersRequest { StudentIds = new[] { existing, fresh } }, TestContext.Current.CancellationToken));

        Assert.Equal(409, ex.StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.False(await db.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id && m.UserId == fresh, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddMembersAsync_SuccessMapsDirectoryFieldsAndUpdatesActivity()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", updatedAt: Now.UtcDateTime.AddDays(-2));
        var student = Guid.NewGuid();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new AuthDirectoryUser(student, "Jane", "Doe", "jane@example.com", "IT777", AuthSecurityConstants.Roles.Student) });
        var sut = CreateSut(factory, directory);

        var result = await sut.AddMembersAsync(owner, project.Id,
            new AddProjectMembersRequest { StudentIds = new[] { student } }, TestContext.Current.CancellationToken);

        var member = Assert.Single(result.Members, m => m.Id == student);
        Assert.Equal("Jane", member.FirstName);
        Assert.Equal("Doe", member.LastName);
        Assert.Equal("jane@example.com", member.Email);
        Assert.Equal("IT777", member.RegistrationNumber);
        Assert.Equal(ProjectMemberRoles.Student, member.MemberRole);
        await using var db = factory.CreateDbContext();
        var persistedProject = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Now.UtcDateTime, persistedProject.UpdatedAt);
        Assert.Equal(Now.UtcDateTime, persistedProject.LastActivityAt);
    }

    [Fact]
    public async Task RemoveStudentAsync_NonOwnerAndMissingProjectAreDistinctFailures()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: student);
        var sut = CreateSut(factory);

        var forbidden = await Assert.ThrowsAsync<ApiException>(() => sut.RemoveStudentAsync(Guid.NewGuid(), project.Id, student, TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<ApiException>(() => sut.RemoveStudentAsync(owner, Guid.NewGuid(), student, TestContext.Current.CancellationToken));

        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal(404, missing.StatusCode);
    }

    [Fact]
    public async Task RemoveStudentAsync_DoesNotRemoveWrongRoleOrSameStudentFromDifferentProject()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var student = Guid.NewGuid();
        var target = await SeedProjectAsync(factory, owner, "Target");
        _ = await SeedProjectAsync(factory, owner, "Other", studentId: student);
        await AddMemberAsync(factory, target.Id, student, ProjectMemberRoles.Supervisor, "Wrong", "Role");
        var sut = CreateSut(factory);

        var ex = await Assert.ThrowsAsync<ApiException>(() => sut.RemoveStudentAsync(owner, target.Id, student, TestContext.Current.CancellationToken));

        Assert.Equal(404, ex.StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.True(await db.ProjectMembers.AnyAsync(m => m.ProjectId == target.Id && m.UserId == student && m.MemberRole == ProjectMemberRoles.Supervisor, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveStudentAsync_NonLeaderPreservesLeaderWhileRemovingTarget()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var leader = Guid.NewGuid();
        var removable = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: leader, leaderId: leader);
        await AddMemberAsync(factory, project.Id, removable, ProjectMemberRoles.Student, "Remove", "Me");
        var sut = CreateSut(factory);

        var result = await sut.RemoveStudentAsync(owner, project.Id, removable, TestContext.Current.CancellationToken);

        Assert.Equal(leader, result.Leader!.Id);
        Assert.DoesNotContain(result.Members, m => m.Id == removable);
        Assert.Contains(result.Members, m => m.Id == leader);
    }

    [Fact]
    public async Task RemoveStudentAsync_LeaderRemovalClearsLeaderAndUpdatesActivity()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var leader = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", studentId: leader, leaderId: leader, updatedAt: Now.UtcDateTime.AddDays(-1));
        var sut = CreateSut(factory);

        var result = await sut.RemoveStudentAsync(owner, project.Id, leader, TestContext.Current.CancellationToken);

        Assert.Null(result.Leader);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Null(persisted.LeaderStudentUserId);
        Assert.Equal(Now.UtcDateTime, persisted.UpdatedAt);
        Assert.Equal(Now.UtcDateTime, persisted.LastActivityAt);
    }

    [Fact]
    public async Task AddMilestoneAsync_RejectsMissingProjectAndNonOwner()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var sut = CreateSut(factory);
        var request = new CreateProjectMilestoneRequest { Title = "M", DueDate = Today.AddDays(2) };

        var missing = await Assert.ThrowsAsync<ApiException>(() => sut.AddMilestoneAsync(owner, Guid.NewGuid(), request, TestContext.Current.CancellationToken));
        var forbidden = await Assert.ThrowsAsync<ApiException>(() => sut.AddMilestoneAsync(Guid.NewGuid(), project.Id, request, TestContext.Current.CancellationToken));

        Assert.Equal(404, missing.StatusCode);
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task AddMilestoneAsync_SuccessTrimsDescriptionSequencesAndRecalculatesAggregate()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        _ = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(3), "M1");
        var sut = CreateSut(factory);

        var result = await sut.AddMilestoneAsync(owner, project.Id,
            new CreateProjectMilestoneRequest { Title = "  M2  ", Description = "  Details  ", DueDate = Today.AddDays(5) }, TestContext.Current.CancellationToken);

        Assert.Equal("M2", result.Title);
        Assert.Equal("Details", result.Description);
        Assert.Equal(2, result.SequenceNo);
        Assert.Equal(ProjectMilestoneStatuses.Planned, result.Status);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Today.AddDays(3), persisted.MilestoneDate);
        Assert.Equal(0, persisted.ProgressPercent);
        Assert.Equal(Now.UtcDateTime, persisted.UpdatedAt);
    }

    [Fact]
    public async Task AddMilestoneAsync_WhitespaceDescriptionPersistsAsNull()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var sut = CreateSut(factory);

        var result = await sut.AddMilestoneAsync(owner, project.Id,
            new CreateProjectMilestoneRequest { Title = "M", Description = "   ", DueDate = Today }, TestContext.Current.CancellationToken);

        Assert.Null(result.Description);
    }

    [Fact]
    public async Task AddMilestoneAsync_ChronologyViolationDoesNotPersistNewMilestone()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        _ = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(5), "M1");
        var sut = CreateSut(factory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.AddMilestoneAsync(owner, project.Id,
            new CreateProjectMilestoneRequest { Title = "Too early", DueDate = Today.AddDays(4) }, TestContext.Current.CancellationToken));

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.ProjectMilestones.CountAsync(m => m.ProjectId == project.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateMilestoneAsync_StatusAndDueDateChangeRecalculatesProgressAndNextOpenDate()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var first = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(2), "M1");
        _ = await SeedMilestoneAsync(factory, project.Id, owner, 2, Today.AddDays(6), "M2");
        var sut = CreateSut(factory);

        var result = await sut.UpdateMilestoneAsync(owner, project.Id, first.Id,
            new UpdateProjectMilestoneRequest(" Done ", " finished ", Today.AddDays(2), ProjectMilestoneStatuses.Completed), TestContext.Current.CancellationToken);

        Assert.Equal("Done", result.Title);
        Assert.Equal("finished", result.Description);
        Assert.Equal(ProjectMilestoneStatuses.Completed, result.Status);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(50, persisted.ProgressPercent);
        Assert.Equal(Today.AddDays(6), persisted.MilestoneDate);
        Assert.Equal(Now.UtcDateTime, persisted.LastActivityAt);
    }

    [Fact]
    public async Task UpdateMilestoneAsync_TitleOnlyChangeDoesNotRecalculateExistingProjectAggregates()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P", progressPercent: 73, milestoneDate: Today.AddDays(9));
        var milestone = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(3), "M1");
        var sut = CreateSut(factory);

        var result = await sut.UpdateMilestoneAsync(owner, project.Id, milestone.Id,
            new UpdateProjectMilestoneRequest("Renamed", null, milestone.DueDate, milestone.Status), TestContext.Current.CancellationToken);

        Assert.Equal("Renamed", result.Title);
        await using var db = factory.CreateDbContext();
        var persisted = await db.Projects.SingleAsync(p => p.Id == project.Id, TestContext.Current.CancellationToken);
        Assert.Equal(73, persisted.ProgressPercent);
        Assert.Equal(Today.AddDays(9), persisted.MilestoneDate);
    }

    [Fact]
    public async Task UpdateMilestoneAsync_DueDateChangeHonorsBothNeighbourBoundaries()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        _ = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(2), "M1");
        var middle = await SeedMilestoneAsync(factory, project.Id, owner, 2, Today.AddDays(5), "M2");
        _ = await SeedMilestoneAsync(factory, project.Id, owner, 3, Today.AddDays(8), "M3");
        var sut = CreateSut(factory);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.UpdateMilestoneAsync(owner, project.Id, middle.Id,
            new UpdateProjectMilestoneRequest("M2", null, Today.AddDays(1), ProjectMilestoneStatuses.Planned), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.UpdateMilestoneAsync(owner, project.Id, middle.Id,
            new UpdateProjectMilestoneRequest("M2", null, Today.AddDays(9), ProjectMilestoneStatuses.Planned), TestContext.Current.CancellationToken));

        await using var db = factory.CreateDbContext();
        Assert.Equal(Today.AddDays(5), (await db.ProjectMilestones.SingleAsync(m => m.Id == middle.Id, TestContext.Current.CancellationToken)).DueDate);
    }

    [Fact]
    public async Task UpdateMilestoneAsync_NonOwnerAndMissingProjectDoNotMutateMilestone()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var project = await SeedProjectAsync(factory, owner, "P");
        var milestone = await SeedMilestoneAsync(factory, project.Id, owner, 1, Today.AddDays(4), "Original");
        var sut = CreateSut(factory);
        var request = new UpdateProjectMilestoneRequest("Changed", null, milestone.DueDate, ProjectMilestoneStatuses.Planned);

        var forbidden = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateMilestoneAsync(Guid.NewGuid(), project.Id, milestone.Id, request, TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateMilestoneAsync(owner, Guid.NewGuid(), milestone.Id, request, TestContext.Current.CancellationToken));

        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal(404, missing.StatusCode);
        await using var db = factory.CreateDbContext();
        Assert.Equal("Original", (await db.ProjectMilestones.SingleAsync(m => m.Id == milestone.Id, TestContext.Current.CancellationToken)).Title);
    }

    [Fact]
    public async Task Dashboard_GetAsync_FiltersSupervisorOrdersProjectsAndAppliesRecentLimit()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        for (var i = 0; i < 6; i++)
        {
            _ = await SeedProjectAsync(factory, owner, $"P{i}", createdAt: Now.UtcDateTime.AddDays(-i), lastActivityAt: Now.UtcDateTime.AddHours(-i));
        }
        _ = await SeedProjectAsync(factory, other, "Hidden", lastActivityAt: Now.UtcDateTime.AddHours(1));
        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));

        var result = await sut.GetAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(6, result.TotalProjects);
        Assert.Equal(6, result.Projects.Count);
        Assert.Equal(5, result.RecentProjects.Count);
        Assert.Equal("P0", result.Projects[0].Title);
        Assert.Equal("P5", result.Projects[5].Title);
        Assert.DoesNotContain(result.Projects, p => p.Title == "Hidden");
        Assert.Equal(result.Projects.Take(5).Select(p => p.Id), result.RecentProjects.Select(p => p.Id));
    }

    [Fact]
    public async Task Dashboard_GetAsync_CountsStatusesCaseInsensitivelyAndKeepsJiraProjectionNeutral()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        _ = await SeedProjectAsync(factory, owner, "Planning", lifecycleStatus: "planning");
        _ = await SeedProjectAsync(factory, owner, "Active", lifecycleStatus: "active");
        _ = await SeedProjectAsync(factory, owner, "Risk", lifecycleStatus: "at_risk");
        _ = await SeedProjectAsync(factory, owner, "Behind", lifecycleStatus: "behind");
        _ = await SeedProjectAsync(factory, owner, "Completed", lifecycleStatus: "completed");
        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));

        var result = await sut.GetAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.PlanningProjects);
        Assert.Equal(1, result.ActiveProjects);
        Assert.Equal(1, result.AtRiskProjects);
        Assert.Equal(1, result.BehindProjects);
        Assert.Equal(1, result.CompletedProjects);
        Assert.Equal(0, result.JiraAtRiskCount);
        Assert.Equal(0, result.JiraBehindCount);
        Assert.All(result.Projects, p => Assert.Equal("UNAVAILABLE", p.JiraHealthIndicator));
    }

    [Fact]
    public async Task Dashboard_GetAsync_UpcomingWindowIncludesBothBoundariesAndExcludesPastFutureNullAndCompleted()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        _ = await SeedProjectAsync(factory, owner, "Today", milestoneDate: Today);
        _ = await SeedProjectAsync(factory, owner, "Day14", milestoneDate: Today.AddDays(14));
        _ = await SeedProjectAsync(factory, owner, "Past", milestoneDate: Today.AddDays(-1));
        _ = await SeedProjectAsync(factory, owner, "Day15", milestoneDate: Today.AddDays(15));
        _ = await SeedProjectAsync(factory, owner, "Null", milestoneDate: null);
        _ = await SeedProjectAsync(factory, owner, "Completed", lifecycleStatus: ProjectLifecycleStatuses.Completed, milestoneDate: Today.AddDays(2));
        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));

        var result = await sut.GetAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.UpcomingMilestonesCount);
    }

    [Fact]
    public async Task Dashboard_GetAsync_ProjectsWithEqualActivityUseCreatedAtAsTieBreakerAndMapMemberCount()
    {
        var factory = new TestProjectDbContextFactory();
        var owner = Guid.NewGuid();
        var older = await SeedProjectAsync(factory, owner, "Older", createdAt: Now.UtcDateTime.AddDays(-2), lastActivityAt: Now.UtcDateTime);
        var newer = await SeedProjectAsync(factory, owner, "Newer", createdAt: Now.UtcDateTime.AddDays(-1), lastActivityAt: Now.UtcDateTime);
        await AddMemberAsync(factory, newer.Id, Guid.NewGuid(), ProjectMemberRoles.Student, "Extra", "Member");
        var sut = new SupervisorDashboardService(factory, new FixedTimeProvider(Now));

        var result = await sut.GetAsync(owner, TestContext.Current.CancellationToken);

        Assert.Equal(newer.Id, result.Projects[0].Id);
        Assert.Equal(older.Id, result.Projects[1].Id);
        Assert.Equal(2, result.Projects[0].MemberCount);
        Assert.Equal(1, result.Projects[1].MemberCount);
    }

    private static ProjectApplicationService CreateSut(TestProjectDbContextFactory factory, IAuthUserDirectoryClient? directory = null) =>
        new(factory, directory ?? Substitute.For<IAuthUserDirectoryClient>(), new FixedTimeProvider(Now));

    private static CreateProjectRequest ValidCreateRequest(Guid studentId) => new()
    {
        Title = "Project",
        Summary = "Summary",
        Batch = "Y3.S1",
        Semester = ProjectSemesters.Semester1,
        StudentIds = new[] { studentId },
        LeaderStudentId = studentId,
        Milestones = new[]
        {
            new CreateProjectMilestoneRequest { Title = "M1", DueDate = Today.AddDays(3) }
        }
    };

    private static AuthDirectoryUser Student(Guid id, string firstName) =>
        new(id, firstName, "Student", $"{firstName.ToLowerInvariant()}@example.com", $"IT{id.ToString("N")[..6]}", AuthSecurityConstants.Roles.Student);

    private static async Task<Project> SeedProjectAsync(
        TestProjectDbContextFactory factory,
        Guid supervisorId,
        string title,
        Guid? studentId = null,
        Guid? leaderId = null,
        string lifecycleStatus = ProjectLifecycleStatuses.Planning,
        int progressPercent = 0,
        DateOnly? milestoneDate = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        DateTime? lastActivityAt = null)
    {
        var created = createdAt ?? Now.UtcDateTime.AddDays(-10);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Title = title,
            Summary = $"Summary-{title}",
            Batch = "Y3.S1",
            Semester = ProjectSemesters.Semester1,
            LifecycleStatus = lifecycleStatus,
            ProgressPercent = progressPercent,
            SupervisorUserId = supervisorId,
            LeaderStudentUserId = leaderId,
            MilestoneDate = milestoneDate,
            CreatedAt = created,
            UpdatedAt = updatedAt ?? created,
            LastActivityAt = lastActivityAt ?? created
        };

        await using var db = factory.CreateDbContext();
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, UserId = supervisorId,
            MemberRole = ProjectMemberRoles.Supervisor, FirstName = "Ada", LastName = "Supervisor",
            Email = "ada@example.com", RegistrationNumber = null,
            CreatedAt = created, UpdatedAt = created
        });
        if (studentId is Guid id)
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, UserId = id,
                MemberRole = ProjectMemberRoles.Student, FirstName = "Sam", LastName = "Student",
                Email = "sam@example.com", RegistrationNumber = "IT001",
                CreatedAt = created.AddMinutes(1), UpdatedAt = created.AddMinutes(1)
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return project;
    }

    private static async Task AddMemberAsync(
        TestProjectDbContextFactory factory,
        Guid projectId,
        Guid userId,
        string role,
        string firstName,
        string lastName,
        string? email = null,
        string? registrationNumber = null)
    {
        await using var db = factory.CreateDbContext();
        db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(), ProjectId = projectId, UserId = userId, MemberRole = role,
            FirstName = firstName, LastName = lastName, Email = email ?? $"{firstName.ToLowerInvariant()}@example.com",
            RegistrationNumber = registrationNumber, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ProjectMilestone> SeedMilestoneAsync(
        TestProjectDbContextFactory factory,
        Guid projectId,
        Guid supervisorId,
        int sequence,
        DateOnly dueDate,
        string title,
        string status = ProjectMilestoneStatuses.Planned)
    {
        var milestone = new ProjectMilestone
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = title, Description = $"D-{title}",
            DueDate = dueDate, Status = status, SequenceNo = sequence, CreatedByUserId = supervisorId,
            CreatedAt = Now.UtcDateTime.AddDays(-1), UpdatedAt = Now.UtcDateTime.AddDays(-1)
        };
        await using var db = factory.CreateDbContext();
        db.ProjectMilestones.Add(milestone);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return milestone;
    }

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options =
            new DbContextOptionsBuilder<ProjectDbContext>()
                .UseInMemoryDatabase($"project-mutation-survivor-hardening-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        public ProjectDbContext CreateDbContext() => new(_options);
        public Task<ProjectDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
