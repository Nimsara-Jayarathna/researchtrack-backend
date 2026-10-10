using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;
using ResearchTrack.SubmissionService.Persistence;

namespace ResearchTrack.SubmissionService.Tests.Services;

public sealed class SubmissionRequirementServiceCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ValidRequest_AuthorizesGetsProfileAndPersistsNormalizedRequirement()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var auth = AuthorizedContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(TestContext.Current.CancellationToken).Returns("Ada Supervisor");
        var sut = CreateSut(factory, auth, profile);

        var result = await sut.CreateAsync(
            projectId,
            userId,
            new SubmissionRequirementCreateRequest(
                " Final Report ", " Description ", Now.AddDays(7), ["PDF", "docx"], 1024 * 1024,
                SubmissionConstants.ResponsibilityMode.AssignedStudent, studentId),
            TestContext.Current.CancellationToken);

        Assert.Equal("Final Report", result.Title);
        Assert.Equal("Description", result.Description);
        Assert.Equal(SubmissionConstants.RequirementStatus.Open, result.Status);
        Assert.Equal(studentId, result.Responsibility.AssignedStudentId);
        await auth.Received(1).EnsureCanManageAsync(projectId, TestContext.Current.CancellationToken);
        await profile.Received(1).GetCurrentUserDisplayNameAsync(TestContext.Current.CancellationToken);
        await using var db = factory.CreateDbContext();
        var persisted = Assert.Single(db.SubmissionRequirements);
        Assert.Equal("docx,pdf", persisted.AllowedFileTypes);
        Assert.Equal("Ada Supervisor", persisted.CreatedByName);
    }

    [Fact]
    public async Task CreateAsync_InvalidTitle_FailsBeforeProfileAndPersistence()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var auth = AuthorizedContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        var sut = CreateSut(factory, auth, profile);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(
            projectId,
            Guid.NewGuid(),
            new SubmissionRequirementCreateRequest(" ", null, null, ["pdf"], 1024,
                SubmissionConstants.ResponsibilityMode.ProjectLeader, null),
            TestContext.Current.CancellationToken));

        await profile.DidNotReceive().GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>());
        await using var db = factory.CreateDbContext();
        Assert.Empty(db.SubmissionRequirements);
    }

    [Fact]
    public async Task UpdateAsync_ArchivedRequirement_IsReadOnly()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Archived);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.UpdateAsync(
            projectId,
            requirement.Id,
            new SubmissionRequirementUpdateRequest("Updated", null, null, ["pdf"], 1024,
                SubmissionConstants.ResponsibilityMode.ProjectLeader, null),
            TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CloseReopenArchive_ChangesRequirementStatus()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Open);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>());
        var ct = TestContext.Current.CancellationToken;

        var closed = await sut.CloseAsync(projectId, requirement.Id, ct);
        Assert.Equal(SubmissionConstants.RequirementStatus.Closed, closed.Status);
        var reopened = await sut.ReopenAsync(projectId, requirement.Id, ct);
        Assert.Equal(SubmissionConstants.RequirementStatus.Open, reopened.Status);
        var archived = await sut.ArchiveAsync(projectId, requirement.Id, ct);
        Assert.Equal(SubmissionConstants.RequirementStatus.Archived, archived.Status);
    }

    [Fact]
    public async Task DeleteAsync_RequirementWithoutHistory_IsRemoved()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Open);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>());

        await sut.DeleteAsync(projectId, requirement.Id, TestContext.Current.CancellationToken);

        await using var db = factory.CreateDbContext();
        Assert.Empty(db.SubmissionRequirements);
    }

    [Fact]
    public async Task GetAsync_MissingRequirement_ReturnsNotFound()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.GetAsync(
            projectId, Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(404, exception.StatusCode);
    }

    private static SubmissionRequirementService CreateSut(
        TestSubmissionDbContextFactory factory,
        IProjectAuthorizationClient auth,
        IUserProfileClient profile) => new(
            factory,
            auth,
            profile,
            new SubmissionOptions { AllowedFileTypes = ["pdf", "docx", "pptx", "zip"] },
            new StorageOptions { MaximumFileSizeBytes = 10 * 1024 * 1024 },
            new FixedTimeProvider(Now));

    private static IProjectAuthorizationClient AuthorizedContext(Guid projectId, Guid studentId)
    {
        var auth = Substitute.For<IProjectAuthorizationClient>();
        auth.GetSubmissionContextAsync(projectId, Arg.Any<CancellationToken>()).Returns(
            new ProjectSubmissionContext(
                new ProjectStudentContext(studentId, "Sam Student", "sam@example.edu", "IT001"),
                [new ProjectStudentContext(studentId, "Sam Student", "sam@example.edu", "IT001")]));
        return auth;
    }

    private static async Task<SubmissionRequirement> SeedRequirementAsync(
        TestSubmissionDbContextFactory factory,
        Guid projectId,
        Guid studentId,
        string status)
    {
        var requirement = new SubmissionRequirement
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = "Final report",
            AllowedFileTypes = "pdf,docx",
            MaxFileSizeBytes = 10 * 1024 * 1024,
            Status = status,
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader,
            AssignedStudentId = studentId,
            AssignedStudentName = "Sam Student",
            CreatedBy = Guid.NewGuid(),
            CreatedByName = "Ada Supervisor",
            CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionRequirements.Add(requirement);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return requirement;
    }

    private sealed class TestSubmissionDbContextFactory : IDbContextFactory<SubmissionDbContext>
    {
        private readonly DbContextOptions<SubmissionDbContext> _options =
            new DbContextOptionsBuilder<SubmissionDbContext>()
                .UseInMemoryDatabase($"submission-requirements-{Guid.NewGuid():N}")
                .Options;
        public SubmissionDbContext CreateDbContext() => new(_options);
        public Task<SubmissionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
