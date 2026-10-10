using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;
using ResearchTrack.SubmissionService.Persistence;

namespace ResearchTrack.SubmissionService.Tests.Services;

public sealed class ResearchSubmissionServiceMockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUploadSessionAsync_ValidRequest_CreatesStorageGrantExactlyOnce()
    {
        var factory = new TestSubmissionDbContextFactory();
        var studentId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var requirementId = await SeedOpenRequirementAsync(factory, projectId, studentId);
        var auth = AuthorizedStudentContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>()).Returns("Sam Student");
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", Arg.Any<CancellationToken>())
            .Returns(new ObjectUploadGrant("https://upload.example.test", Now.AddMinutes(5), new Dictionary<string, string> { ["Content-Type"] = "application/pdf" }));
        var sut = CreateSut(factory, auth, profile, storage);

        var result = await sut.CreateUploadSessionAsync(projectId, requirementId, studentId,
            new CreateUploadSessionRequest("report.pdf", "application/pdf", 1024, "v1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.VersionNumber);
        Assert.Equal("https://upload.example.test", result.UploadUrl);
        await storage.Received(1).CreateUploadGrantAsync(
            Arg.Is<string>(key => key.StartsWith("pending/", StringComparison.Ordinal)),
            "application/pdf", Arg.Any<CancellationToken>());
        await profile.Received(1).GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>());
        await using var db = factory.CreateDbContext();
        var session = Assert.Single(db.SubmissionUploadSessions);
        Assert.Equal("ACTIVE", session.ActiveSlot);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Pending, session.Status);
    }

    [Fact]
    public async Task CreateUploadSessionAsync_InvalidFile_DoesNotCallProfileOrStorage()
    {
        var factory = new TestSubmissionDbContextFactory();
        var studentId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var requirementId = await SeedOpenRequirementAsync(factory, projectId, studentId);
        var auth = AuthorizedStudentContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        var storage = Substitute.For<IObjectStorageService>();
        var sut = CreateSut(factory, auth, profile, storage);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateUploadSessionAsync(projectId, requirementId, studentId,
            new CreateUploadSessionRequest("malware.exe", "application/octet-stream", 1024, null),
            TestContext.Current.CancellationToken));

        await storage.DidNotReceive().CreateUploadGrantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await profile.DidNotReceive().GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUploadSessionAsync_StorageGrantFailure_MarksSessionFailed()
    {
        var factory = new TestSubmissionDbContextFactory();
        var studentId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var requirementId = await SeedOpenRequirementAsync(factory, projectId, studentId);
        var auth = AuthorizedStudentContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>()).Returns("Sam Student");
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ObjectUploadGrant>(new InvalidOperationException("storage unavailable")));
        var sut = CreateSut(factory, auth, profile, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateUploadSessionAsync(projectId, requirementId, studentId,
            new CreateUploadSessionRequest("report.pdf", "application/pdf", 1024, null),
            TestContext.Current.CancellationToken));

        await using var db = factory.CreateDbContext();
        var session = Assert.Single(db.SubmissionUploadSessions);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Failed, session.Status);
        Assert.Null(session.ActiveSlot);
        Assert.NotNull(session.FailureReason);
    }

    private static IProjectAuthorizationClient AuthorizedStudentContext(Guid projectId, Guid studentId)
    {
        var auth = Substitute.For<IProjectAuthorizationClient>();
        auth.GetSubmissionContextAsync(projectId, Arg.Any<CancellationToken>()).Returns(
            new ProjectSubmissionContext(
                new ProjectStudentContext(studentId, "Sam Student", "sam@example.com", "IT001"),
                [new ProjectStudentContext(studentId, "Sam Student", "sam@example.com", "IT001")]));
        return auth;
    }

    private static ResearchSubmissionService CreateSut(
        IDbContextFactory<SubmissionDbContext> factory,
        IProjectAuthorizationClient auth,
        IUserProfileClient profile,
        IObjectStorageService storage) => new(
            factory, auth, profile, storage,
            new SubmissionOptions { AllowedFileTypes = ["pdf", "docx", "pptx", "zip"], UploadSessionLifetimeMinutes = 10 },
            new FixedTimeProvider(Now), NullLogger<ResearchSubmissionService>.Instance);

    private static async Task<Guid> SeedOpenRequirementAsync(TestSubmissionDbContextFactory factory, Guid projectId, Guid studentId)
    {
        var requirement = new SubmissionRequirement
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = "Final report", AllowedFileTypes = "pdf,docx,pptx,zip",
            MaxFileSizeBytes = 10 * 1024 * 1024, Status = SubmissionConstants.RequirementStatus.Open,
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader,
            AssignedStudentId = studentId, AssignedStudentName = "Sam Student", CreatedBy = Guid.NewGuid(),
            CreatedByName = "Supervisor", CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionRequirements.Add(requirement);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return requirement.Id;
    }

    private sealed class TestSubmissionDbContextFactory : IDbContextFactory<SubmissionDbContext>
    {
        private readonly DbContextOptions<SubmissionDbContext> _options = new DbContextOptionsBuilder<SubmissionDbContext>()
            .UseInMemoryDatabase($"submission-mock-tests-{Guid.NewGuid():N}").Options;
        public SubmissionDbContext CreateDbContext() => new(_options);
        public Task<SubmissionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
