using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;
using ProjectApplicationService = ResearchTrack.ProjectService.Features.Projects.ProjectService;

namespace ResearchTrack.ProjectService.MutationTests.Services;

public sealed class ProjectServiceMockInteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ValidSupervisor_ResolvesStudentsOnceAndPersistsProjectGraph()
    {
        var cancellationToken = CancellationToken.None;
        var factory = new TestProjectDbContextFactory();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        directory.GetCurrentUserAsync(cancellationToken).Returns(
            new AuthDirectoryUser(
                supervisorId,
                "Ada",
                "Supervisor",
                "ada@example.com",
                null,
                AuthSecurityConstants.Roles.Supervisor));
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), cancellationToken).Returns(
            new[]
            {
                new AuthDirectoryUser(
                    studentId,
                    "Sam",
                    "Student",
                    "sam@example.com",
                    "IT001",
                    AuthSecurityConstants.Roles.Student)
            });

        var sut = new ProjectApplicationService(factory, directory, new FixedTimeProvider(Now));

        var result = await sut.CreateAsync(supervisorId, ValidRequest(studentId), cancellationToken);

        Assert.Equal("ResearchTrack", result.Title);
        Assert.Single(result.Students);
        await directory.Received(1).GetCurrentUserAsync(cancellationToken);
        await directory.Received(1).ResolveStudentsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(studentId)),
            cancellationToken);

        await using var db = factory.CreateDbContext();
        Assert.Single(db.Projects);
        Assert.Equal(2, db.ProjectMembers.Count());
        Assert.Single(db.ProjectMilestones);
    }

    [Fact]
    public async Task CreateAsync_NonSupervisor_DoesNotResolveStudentsOrCreateDbContext()
    {
        var cancellationToken = CancellationToken.None;
        var factory = Substitute.For<IDbContextFactory<ProjectDbContext>>();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var userId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        directory.GetCurrentUserAsync(cancellationToken).Returns(
            new AuthDirectoryUser(
                userId,
                "Sam",
                "Student",
                "sam@example.com",
                "IT001",
                AuthSecurityConstants.Roles.Student));

        var sut = new ProjectApplicationService(factory, directory, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ApiException>(() =>
            sut.CreateAsync(userId, ValidRequest(studentId), cancellationToken));

        await directory.DidNotReceive().ResolveStudentsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            cancellationToken);
        await factory.DidNotReceive().CreateDbContextAsync(cancellationToken);
    }

    [Fact]
    public async Task CreateAsync_UnresolvedStudent_FailsBeforePersistence()
    {
        var cancellationToken = CancellationToken.None;
        var factory = Substitute.For<IDbContextFactory<ProjectDbContext>>();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        directory.GetCurrentUserAsync(cancellationToken).Returns(
            new AuthDirectoryUser(
                supervisorId,
                "Ada",
                "Supervisor",
                "ada@example.com",
                null,
                AuthSecurityConstants.Roles.Supervisor));
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), cancellationToken)
            .Returns(Array.Empty<AuthDirectoryUser>());

        var sut = new ProjectApplicationService(factory, directory, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ApiValidationException>(() =>
            sut.CreateAsync(supervisorId, ValidRequest(studentId), cancellationToken));

        await factory.DidNotReceive().CreateDbContextAsync(cancellationToken);
    }

    private static CreateProjectRequest ValidRequest(Guid studentId) => new()
    {
        Title = "ResearchTrack",
        Summary = "Project",
        Batch = "Y3.S1",
        Semester = ProjectSemesters.Semester1,
        StudentIds = [studentId],
        LeaderStudentId = studentId,
        Milestones =
        [
            new CreateProjectMilestoneRequest
            {
                Title = "M1",
                DueDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(7)
            }
        ]
    };

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options =
            new DbContextOptionsBuilder<ProjectDbContext>()
                .UseInMemoryDatabase($"project-mock-tests-{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        public ProjectDbContext CreateDbContext() => new(_options);

        public Task<ProjectDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
