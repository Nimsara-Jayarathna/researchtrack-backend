using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ResearchTrack.SubmissionService.Configuration;
using ResearchTrack.SubmissionService.Contracts;
using ResearchTrack.SubmissionService.Domain;
using ResearchTrack.SubmissionService.Features;
using ResearchTrack.SubmissionService.Infrastructure;
using ResearchTrack.SubmissionService.Persistence;

namespace ResearchTrack.SubmissionService.Tests.Services;

public sealed class ResearchSubmissionSuccessfulFlowCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAndCompleteUploadSession_FirstVersion_PersistsSubmissionVersionAndCompletesSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, ct);
        var auth = AuthorizedContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(ct).Returns("Sam Student");
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct)
            .Returns(new ObjectUploadGrant("https://upload", Now.AddMinutes(5), new Dictionary<string, string>()));
        storage.GetMetadataAsync(Arg.Any<string>(), ct)
            .Returns(new StoredObjectMetadata(1024, "application/pdf", "etag-1"));
        var sut = CreateSut(factory, auth, profile, storage);

        var upload = await sut.CreateUploadSessionAsync(
            projectId,
            requirement.Id,
            studentId,
            new CreateUploadSessionRequest(" report.pdf ", "application/pdf", 1024, " first version "),
            ct);
        var completed = await sut.CompleteUploadSessionAsync(projectId, upload.UploadSessionId, studentId, ct);

        Assert.Equal(1, upload.VersionNumber);
        Assert.Equal(SubmissionConstants.SubmissionStatus.PendingReview, completed.Status);
        Assert.Equal(1, completed.VersionCount);
        var version = Assert.Single(completed.Versions);
        Assert.Equal("report.pdf", version.OriginalFileName);
        Assert.Equal("first version", version.SubmissionNote);
        Assert.True(version.IsCurrent);
        await storage.Received(1).PromoteAsync(Arg.Is<string>(x => x.StartsWith("pending/", StringComparison.Ordinal)), Arg.Any<string>(), ct);
        await storage.Received().DeleteIfExistsAsync(Arg.Is<string>(x => x.StartsWith("pending/", StringComparison.Ordinal)), Arg.Any<CancellationToken>());

        await using var db = factory.CreateDbContext();
        Assert.Single(await db.ResearchSubmissions.ToListAsync(ct));
        Assert.Single(await db.SubmissionVersions.ToListAsync(ct));
        var session = await db.SubmissionUploadSessions.SingleAsync(ct);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Completed, session.Status);
        Assert.Null(session.ActiveSlot);
    }

    [Fact]
    public async Task CompleteUploadSession_AlreadyCompleted_ReturnsExistingResponseWithoutPromotingAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var seeded = await SeedCompletedSubmissionAsync(factory, projectId, studentId, ct);
        var storage = Substitute.For<IObjectStorageService>();
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>(), storage);

        var response = await sut.CompleteUploadSessionAsync(projectId, seeded.SessionId, studentId, ct);

        Assert.Equal(seeded.SubmissionId, response.Id);
        Assert.Equal(1, response.VersionCount);
        await storage.DidNotReceive().PromoteAsync(Arg.Any<string>(), Arg.Any<string>(), ct);
    }

    [Fact]
    public async Task CompleteUploadSession_ExpiredSession_MarksExpiredAndDeletesTemporaryObject()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, ct);
        var session = await SeedPendingSessionAsync(factory, projectId, requirement.Id, studentId, Now.AddMinutes(-1), ct);
        var storage = Substitute.For<IObjectStorageService>();
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>(), storage);

        await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(() =>
            sut.CompleteUploadSessionAsync(projectId, session.Id, studentId, ct));

        await storage.Received(1).DeleteIfExistsAsync(session.TemporaryObjectKey, Arg.Any<CancellationToken>());
        await using var db = factory.CreateDbContext();
        var stored = await db.SubmissionUploadSessions.SingleAsync(x => x.Id == session.Id, ct);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Expired, stored.Status);
        Assert.Null(stored.ActiveSlot);
    }

    [Fact]
    public async Task CompleteUploadSession_MissingStorageObject_ReturnsValidationErrorWithoutPromotion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, ct);
        var session = await SeedPendingSessionAsync(factory, projectId, requirement.Id, studentId, Now.AddMinutes(5), ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.GetMetadataAsync(session.TemporaryObjectKey, ct).Returns((StoredObjectMetadata?)null);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>(), storage);

        await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiValidationException>(() =>
            sut.CompleteUploadSessionAsync(projectId, session.Id, studentId, ct));

        await storage.DidNotReceive().PromoteAsync(Arg.Any<string>(), Arg.Any<string>(), ct);
    }

    [Fact]
    public async Task ReviewAsync_ApproveCurrentVersion_PersistsReviewAndApprovalState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var seeded = await SeedCompletedSubmissionAsync(factory, projectId, studentId, ct, sessionCompleted: false);
        var auth = AuthorizedContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(ct).Returns("Supervisor Name");
        var sut = CreateSut(factory, auth, profile, Substitute.For<IObjectStorageService>());
        var reviewerId = Guid.NewGuid();

        var response = await sut.ReviewAsync(projectId, seeded.SubmissionId, reviewerId,
            new CreateSubmissionReviewRequest(seeded.VersionId, SubmissionConstants.ReviewDecision.Approved, null), ct);

        Assert.Equal(SubmissionConstants.SubmissionStatus.Approved, response.Status);
        Assert.Equal(seeded.VersionId, response.ApprovedVersionId);
        Assert.NotNull(response.ApprovedAt);
        var version = Assert.Single(response.Versions);
        Assert.True(version.IsApproved);
        Assert.NotNull(version.Review);
        Assert.Equal(SubmissionConstants.ReviewDecision.Approved, version.Review!.Decision);
        await auth.Received(1).EnsureCanManageAsync(projectId, ct);
    }

    [Fact]
    public async Task ReviewAsync_RequestChanges_PersistsFeedbackAndAllowsRevisionUploadSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var seeded = await SeedCompletedSubmissionAsync(factory, projectId, studentId, ct, sessionCompleted: false);
        var auth = AuthorizedContext(projectId, studentId);
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(ct).Returns("Supervisor Name");
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct)
            .Returns(new ObjectUploadGrant("https://upload-v2", Now.AddMinutes(5), new Dictionary<string, string>()));
        var sut = CreateSut(factory, auth, profile, storage);

        var reviewed = await sut.ReviewAsync(projectId, seeded.SubmissionId, Guid.NewGuid(),
            new CreateSubmissionReviewRequest(seeded.VersionId, SubmissionConstants.ReviewDecision.ChangesRequested, " Please revise "), ct);
        var revision = await sut.CreateUploadSessionAsync(projectId, seeded.RequirementId, studentId,
            new CreateUploadSessionRequest("revision.pdf", "application/pdf", 1024, "v2"), ct);

        Assert.Equal(SubmissionConstants.SubmissionStatus.ChangesRequested, reviewed.Status);
        Assert.Equal("Please revise", Assert.Single(reviewed.Versions).Review!.Feedback);
        Assert.Equal(2, revision.VersionNumber);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ExistingVersion_ReturnsStorageGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteSubmissionFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var seeded = await SeedCompletedSubmissionAsync(factory, projectId, studentId, ct, sessionCompleted: false);
        var auth = AuthorizedContext(projectId, studentId);
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateDownloadGrantAsync("projects/final/report.pdf", "report.pdf", true, ct)
            .Returns(new ObjectDownloadGrant("https://download", Now.AddMinutes(3)));
        var sut = CreateSut(factory, auth, Substitute.For<IUserProfileClient>(), storage);

        var response = await sut.GetDownloadUrlAsync(projectId, seeded.SubmissionId, seeded.VersionId, true, ct);

        Assert.Equal("https://download", response.Url);
        await auth.Received(1).EnsureCanAccessAsync(projectId, ct);
    }

    [Fact]
    public async Task ListAndGetAsync_ExistingSubmission_ReturnMappedVersionAndRequirement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = InMemorySubmissionFactory.Create();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var seeded = await SeedCompletedSubmissionAsync(factory, projectId, studentId, ct, sessionCompleted: false);
        var sut = CreateSut(factory, AuthorizedContext(projectId, studentId), Substitute.For<IUserProfileClient>(), Substitute.For<IObjectStorageService>());

        var list = await sut.ListAsync(projectId, ct);
        var single = await sut.GetAsync(projectId, seeded.SubmissionId, ct);

        Assert.Single(list);
        Assert.Equal(seeded.SubmissionId, single.Id);
        Assert.Equal("Final report", single.Requirement.Title);
        Assert.Single(single.Versions);
    }

    private static ResearchSubmissionService CreateSut(
        IDbContextFactory<SubmissionDbContext> factory,
        IProjectAuthorizationClient auth,
        IUserProfileClient profile,
        IObjectStorageService storage) => new(
            factory,
            auth,
            profile,
            storage,
            new SubmissionOptions { AllowedFileTypes = ["pdf", "docx", "pptx", "zip"], UploadSessionLifetimeMinutes = 10 },
            new FixedTimeProvider(Now),
            NullLogger<ResearchSubmissionService>.Instance);

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
        IDbContextFactory<SubmissionDbContext> factory,
        Guid projectId,
        Guid studentId,
        CancellationToken ct)
    {
        var requirement = new SubmissionRequirement
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Title = "Final report", Description = "Submit final report",
            DueAt = Now.AddDays(1), AllowedFileTypes = "pdf,docx", MaxFileSizeBytes = 10 * 1024 * 1024,
            Status = SubmissionConstants.RequirementStatus.Open,
            ResponsibilityMode = SubmissionConstants.ResponsibilityMode.ProjectLeader,
            AssignedStudentId = studentId, AssignedStudentName = "Sam Student",
            CreatedBy = Guid.NewGuid(), CreatedByName = "Supervisor", CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionRequirements.Add(requirement);
        await db.SaveChangesAsync(ct);
        return requirement;
    }

    private static async Task<SubmissionUploadSession> SeedPendingSessionAsync(
        SqliteSubmissionFactory factory,
        Guid projectId,
        Guid requirementId,
        Guid studentId,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        var session = new SubmissionUploadSession
        {
            Id = Guid.NewGuid(), ProjectId = projectId, RequirementId = requirementId,
            SubmissionId = Guid.NewGuid(), VersionId = Guid.NewGuid(), ExpectedVersionNumber = 1,
            TemporaryObjectKey = "pending/test", FinalObjectKey = "projects/final/test",
            OriginalFileName = "report.pdf", FileExtension = "pdf", ExpectedContentType = "application/pdf",
            DeclaredFileSizeBytes = 1024, ExpectedMaxFileSizeBytes = 10 * 1024 * 1024,
            CreatedBy = studentId, CreatedByName = "Sam Student", Status = SubmissionConstants.UploadSessionStatus.Pending,
            ActiveSlot = "ACTIVE", ExpiresAt = expiresAt, CreatedAt = Now
        };
        await using var db = factory.CreateDbContext();
        db.SubmissionUploadSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    private static async Task<SeededSubmission> SeedCompletedSubmissionAsync(
        IDbContextFactory<SubmissionDbContext> factory,
        Guid projectId,
        Guid studentId,
        CancellationToken ct,
        bool sessionCompleted = true)
    {
        var requirement = await SeedRequirementAsync(factory, projectId, studentId, ct);
        var submissionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.ResearchSubmissions.Add(new ResearchSubmission
        {
            Id = submissionId, ProjectId = projectId, RequirementId = requirement.Id,
            Status = SubmissionConstants.SubmissionStatus.PendingReview,
            CurrentVersionId = versionId, VersionCount = 1, LastSubmittedAt = Now, CreatedAt = Now
        });
        db.SubmissionVersions.Add(new SubmissionVersion
        {
            Id = versionId, SubmissionId = submissionId, VersionNumber = 1,
            ObjectKey = "projects/final/report.pdf", OriginalFileName = "report.pdf", FileExtension = "pdf",
            ContentType = "application/pdf", FileSizeBytes = 1024, ObjectETag = "etag",
            UploadedBy = studentId, UploadedByName = "Sam Student", SubmitterRoleSnapshot = "PROJECT_LEADER",
            ResponsibilityModeSnapshot = SubmissionConstants.ResponsibilityMode.ProjectLeader,
            SubmittedAt = Now, IsLate = false
        });
        db.SubmissionUploadSessions.Add(new SubmissionUploadSession
        {
            Id = sessionId, ProjectId = projectId, RequirementId = requirement.Id, SubmissionId = submissionId,
            VersionId = versionId, ExpectedVersionNumber = 1, TemporaryObjectKey = "pending/already",
            FinalObjectKey = "projects/final/report.pdf", OriginalFileName = "report.pdf", FileExtension = "pdf",
            ExpectedContentType = "application/pdf", DeclaredFileSizeBytes = 1024,
            ExpectedMaxFileSizeBytes = 10 * 1024 * 1024, CreatedBy = studentId, CreatedByName = "Sam Student",
            Status = sessionCompleted ? SubmissionConstants.UploadSessionStatus.Completed : SubmissionConstants.UploadSessionStatus.Failed,
            ActiveSlot = null, ExpiresAt = Now.AddMinutes(5), CreatedAt = Now, CompletedAt = sessionCompleted ? Now : null
        });
        await db.SaveChangesAsync(ct);
        return new SeededSubmission(requirement.Id, submissionId, versionId, sessionId);
    }

    private sealed record SeededSubmission(Guid RequirementId, Guid SubmissionId, Guid VersionId, Guid SessionId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemorySubmissionFactory : IDbContextFactory<SubmissionDbContext>, IAsyncDisposable
    {
        private readonly DbContextOptions<SubmissionDbContext> _options;

        private InMemorySubmissionFactory(DbContextOptions<SubmissionDbContext> options)
        {
            _options = options;
        }

        public static InMemorySubmissionFactory Create()
        {
            var options = new DbContextOptionsBuilder<SubmissionDbContext>()
                .UseInMemoryDatabase($"submission-list-get-{Guid.NewGuid():N}")
                .Options;
            return new InMemorySubmissionFactory(options);
        }

        public SubmissionDbContext CreateDbContext() => new(_options);

        public Task<SubmissionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SqliteSubmissionFactory : IDbContextFactory<SubmissionDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SubmissionDbContext> _options;
        private SqliteSubmissionFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<SubmissionDbContext>().UseSqlite(connection).Options;
        }
        public static async Task<SqliteSubmissionFactory> CreateAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(ct);
            var factory = new SqliteSubmissionFactory(connection);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync(ct);
            return factory;
        }
        public SubmissionDbContext CreateDbContext() => new(_options);
        public Task<SubmissionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
