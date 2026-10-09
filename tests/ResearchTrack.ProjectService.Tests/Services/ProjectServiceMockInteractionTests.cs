using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Features.Projects;
using ResearchTrack.ProjectService.Infrastructure;
using ResearchTrack.ProjectService.Persistence;

namespace ResearchTrack.ProjectService.Tests.Services;

public sealed class ProjectServiceMockInteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ValidSupervisor_ResolvesStudentsOnceAndPersistsProjectGraph()
    {
        var factory = new TestProjectDbContextFactory();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        directory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(supervisorId, "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Supervisor));
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(
            new[] { new AuthDirectoryUser(studentId, "Sam", "Student", "sam@example.com", "IT001", AuthSecurityConstants.Roles.Student) });
        var sut = new ProjectService(factory, directory, new FixedTimeProvider(Now));

        var result = await sut.CreateAsync(supervisorId, ValidRequest(studentId), TestContext.Current.CancellationToken);

        Assert.Equal("ResearchTrack", result.Title);
        Assert.Single(result.Students);
        await directory.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
        await directory.Received(1).ResolveStudentsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(studentId)),
            Arg.Any<CancellationToken>());
        await using var db = factory.CreateDbContext();
        Assert.Single(db.Projects);
        Assert.Equal(2, db.ProjectMembers.Count());
        Assert.Single(db.ProjectMilestones);
    }

    [Fact]
    public async Task CreateAsync_NonSupervisor_DoesNotResolveStudentsOrCreateDbContext()
    {
        var factory = Substitute.For<IDbContextFactory<ProjectDbContext>>();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var userId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        directory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(userId, "Sam", "Student", "sam@example.com", "IT001", AuthSecurityConstants.Roles.Student));
        var sut = new ProjectService(factory, directory, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(userId, ValidRequest(studentId), TestContext.Current.CancellationToken));

        await directory.DidNotReceive().ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_UnresolvedStudent_FailsBeforePersistence()
    {
        var factory = Substitute.For<IDbContextFactory<ProjectDbContext>>();
        var directory = Substitute.For<IAuthUserDirectoryClient>();
        var supervisorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        directory.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(
            new AuthDirectoryUser(supervisorId, "Ada", "Supervisor", "ada@example.com", null, AuthSecurityConstants.Roles.Supervisor));
        directory.ResolveStudentsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<AuthDirectoryUser>());
        var sut = new ProjectService(factory, directory, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(supervisorId, ValidRequest(studentId), TestContext.Current.CancellationToken));

        factory.DidNotReceive().CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    private static CreateProjectRequest ValidRequest(Guid studentId) => new()
    {
        Title = "ResearchTrack", Summary = "Project", Batch = "Y3.S1", Semester = "1",
        StudentIds = [studentId], LeaderStudentId = studentId,
        Milestones = [new CreateProjectMilestoneRequest { Title = "M1", DueDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(7) }]
    };

    private sealed class TestProjectDbContextFactory : IDbContextFactory<ProjectDbContext>
    {
        private readonly DbContextOptions<ProjectDbContext> _options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseInMemoryDatabase($"project-mock-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        public ProjectDbContext CreateDbContext() => new(_options);
        public Task<ProjectDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
