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

public sealed class ResearchSubmissionCoverageHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateUploadSessionAsync_MissingRequirement_ReturnsNotFoundBeforeStorage()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var storage = Substitute.For<IObjectStorageService>();
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), storage);

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(
            projectId, Guid.NewGuid(), studentId,
            new CreateUploadSessionRequest("report.pdf", "application/pdf", 1000, null),
            TestContext.Current.CancellationToken));

        Assert.Equal(404, exception.StatusCode);
        await storage.DidNotReceive().CreateUploadGrantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUploadSessionAsync_ClosedRequirement_ReturnsConflict()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Closed);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(
            projectId, requirement.Id, studentId,
            new CreateUploadSessionRequest("report.pdf", "application/pdf", 1000, null),
            TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CreateUploadSessionAsync_ExistingPendingSubmissionWithoutChangeRequest_ReturnsConflict()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Open);
        await SeedSubmissionAsync(factory, projectId, requirement.Id, SubmissionConstants.SubmissionStatus.PendingReview);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(
            projectId, requirement.Id, studentId,
            new CreateUploadSessionRequest("report.pdf", "application/pdf", 1000, null),
            TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task CompleteUploadSessionAsync_MissingSession_ReturnsNotFound()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(
            projectId, Guid.NewGuid(), studentId, TestContext.Current.CancellationToken));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task CompleteUploadSessionAsync_DifferentCreator_IsForbidden()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, creator, SubmissionConstants.RequirementStatus.Open);
        var session = await SeedUploadSessionAsync(factory, projectId, requirement.Id, creator, SubmissionConstants.UploadSessionStatus.Pending);
        var sut = CreateSut(factory, AuthorizedContext(projectId, creator), Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(
            projectId, session.Id, Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task CompleteUploadSessionAsync_FailedSession_ReturnsConflict()
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, SubmissionConstants.RequirementStatus.Open);
        var session = await SeedUploadSessionAsync(factory, projectId, requirement.Id, studentId, SubmissionConstants.UploadSessionStatus.Failed);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(
            projectId, session.Id, studentId, TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.StatusCode);
    }

    [Theory]
    [InlineData("BOGUS", "feedback")]
    [InlineData("CHANGES_REQUESTED", null)]
    [InlineData("REJECTED", "")]
    public async Task ReviewAsync_InvalidDecisionOrFeedback_IsRejectedBeforeLookup(string decision, string? feedback)
    {
        var factory = new TestSubmissionDbContextFactory();
        var projectId = Guid.NewGuid();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var storage = Substitute.For<IObjectStorageService>();
        var sut = CreateSut(factory, auth, storage);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.ReviewAsync(
            projectId, Guid.NewGuid(), Guid.NewGuid(),
            new CreateSubmissionReviewRequest(Guid.NewGuid(), decision, feedback),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReviewAsync_EmptyVersionId_IsValidationError()
    {
        var sut = CreateSut(new TestSubmissionDbContextFactory(), Substitute.For<IProjectAuthorizationClient>(), Substitute.For<IObjectStorageService>());
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.ReviewAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new CreateSubmissionReviewRequest(Guid.Empty, SubmissionConstants.ReviewDecision.Approved, null),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDownloadUrlAsync_MissingSubmission_ReturnsNotFoundAfterAuthorization()
    {
        var factory = new TestSubmissionDbContextFactory();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var projectId = Guid.NewGuid();
        var sut = CreateSut(factory, auth, Substitute.For<IObjectStorageService>());

        var exception = await Assert.ThrowsAsync<ApiException>(() => sut.GetDownloadUrlAsync(
            projectId, Guid.NewGuid(), Guid.NewGuid(), false, TestContext.Current.CancellationToken));

        Assert.Equal(404, exception.StatusCode);
        await auth.Received(1).EnsureCanAccessAsync(projectId, TestContext.Current.CancellationToken);
    }

    private static ResearchSubmissionService CreateSut(
        IDbContextFactory<SubmissionDbContext> factory,
        IProjectAuthorizationClient auth,
        IObjectStorageService storage)
    {
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>()).Returns("Supervisor");
        return new ResearchSubmissionService(
            factory, auth, profile, storage,
            new SubmissionOptions { AllowedFileTypes = ["pdf", "docx", "pptx", "zip"], UploadSessionLifetimeMinutes = 10 },
            new FixedTimeProvider(Now), NullLogger<ResearchSubmissionService>.Instance);
    }

    private static IProjectAuthorizationClient AuthorizedContext(Guid projectId, Guid studentId)
    {
        var auth = Substitute.For<IProjectAuthorizationClient>();
        auth.GetSubmissionContextAsync(projectId, Arg.Any<CancellationToken>()).Returns(
            new ProjectSubmissionContext(
                new ProjectStudentContext(studentId, "Sam Student", "sam@example.com", "IT001"),
                [new ProjectStudentContext(studentId, "Sam Student", "sam@example.com", "IT001")]));
        return auth;
    }

    private static async Task<SubmissionRequirement> SeedRequirementAsync(
        TestSubmissionDbContextFactory factory, Guid projectId, Guid studentId, string status)
    {
        var requirement = new SubmissionRequirement
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = "Final report",
            AllowedFileTypes = "pdf,docx", MaxFileSizeBytes = 10 * 1024 * 1024,
            Status = status, ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader,
            AssignedStudentId = studentId, AssignedStudentName = "Sam Student",
            CreatedBy = Guid.NewGuid(), CreatedByName = "Supervisor", CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionRequirements.Add(requirement);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return requirement;
    }

    private static async Task<ResearchSubmission> SeedSubmissionAsync(
        TestSubmissionDbContextFactory factory, Guid projectId, Guid requirementId, string status)
    {
        var versionId = Guid.NewGuid();
        var submission = new ResearchSubmission
        {
            Id = Guid.NewGuid(), ProjectId = projectId, RequirementId = requirementId,
            Status = status, CurrentVersionId = versionId, VersionCount = 1,
            LastSubmittedAt = Now, CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.ResearchSubmissions.Add(submission);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return submission;
    }

    private static async Task<SubmissionUploadSession> SeedUploadSessionAsync(
        TestSubmissionDbContextFactory factory, Guid projectId, Guid requirementId, Guid creator, string status)
    {
        var session = new SubmissionUploadSession
        {
            Id = Guid.NewGuid(), ProjectId = projectId, RequirementId = requirementId,
            SubmissionId = Guid.NewGuid(), VersionId = Guid.NewGuid(), ExpectedVersionNumber = 1,
            TemporaryObjectKey = "pending/test", FinalObjectKey = "projects/final",
            OriginalFileName = "report.pdf", FileExtension = "pdf", ExpectedContentType = "application/pdf",
            DeclaredFileSizeBytes = 1000, ExpectedMaxFileSizeBytes = 10 * 1024 * 1024,
            CreatedBy = creator, CreatedByName = "Sam Student", Status = status,
            ActiveSlot = status == SubmissionConstants.UploadSessionStatus.Pending ? "ACTIVE" : null,
            ExpiresAt = Now.AddMinutes(10), CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionUploadSessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session;
    }

    private sealed class TestSubmissionDbContextFactory : IDbContextFactory<SubmissionDbContext>
    {
        private readonly DbContextOptions<SubmissionDbContext> _options =
            new DbContextOptionsBuilder<SubmissionDbContext>()
                .UseInMemoryDatabase($"submission-coverage-hardening-{Guid.NewGuid():N}")
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
