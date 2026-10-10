using Microsoft.Data.Sqlite;
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

public sealed class SubmissionMutationFinalHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);
    private const long MaxBytes = 10 * 1024 * 1024;

    [Fact]
    public async Task Requirement_Create_EnforcesExactDueAndSizeBoundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var auth = Auth(projectId, studentId); var profile = Profile("Supervisor");
        var sut = RequirementSut(factory, auth, profile);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(projectId, Guid.NewGuid(),
            new("R", null, Now, ["pdf"], 1, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct));
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(projectId, Guid.NewGuid(),
            new("R", null, Now.AddMinutes(1), ["pdf"], 0, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct));
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(projectId, Guid.NewGuid(),
            new("R", null, Now.AddMinutes(1), ["pdf"], MaxBytes + 1, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct));

        var ok = await sut.CreateAsync(projectId, Guid.NewGuid(),
            new(" R ", "  ", Now.AddSeconds(1), [".PDF", "pdf"], MaxBytes, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct);
        Assert.Equal("R", ok.Title);
        Assert.Null(ok.Description);
        Assert.Equal(MaxBytes, ok.MaxFileSizeBytes);
        Assert.Equal(new[] { "pdf" }, ok.AllowedFileTypes);
    }

    [Fact]
    public async Task Requirement_Update_AllowsSubMinuteDueAdjustmentButRejectsMeaningfulPastChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var requirement = await SeedRequirement(factory, projectId, studentId, Now.AddHours(-2), ct);
        var sut = RequirementSut(factory, Auth(projectId, studentId), Profile("Supervisor"));

        var tolerated = await sut.UpdateAsync(projectId, requirement.Id,
            new("Updated", null, requirement.DueAt!.Value.AddSeconds(30), ["pdf"], 1024, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct);
        Assert.Equal(requirement.DueAt.Value.AddSeconds(30), tolerated.DueAt);

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.UpdateAsync(projectId, requirement.Id,
            new("Updated", null, Now.AddMinutes(-5), ["pdf"], 1024, SubmissionConstants.ResponsibilityMode.ProjectLeader, null), ct));
    }

    [Fact]
    public async Task Requirement_Delete_RejectsSubmissionHistoryAndActiveSessionIndependently()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var first = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        await using (var db = factory.CreateDbContext())
        {
            db.ResearchSubmissions.Add(new ResearchSubmission { Id = Guid.NewGuid(), ProjectId = projectId, RequirementId = first.Id, Status = SubmissionConstants.SubmissionStatus.PendingReview, VersionCount = 1, LastSubmittedAt = Now, CreatedAt = Now });
            await db.SaveChangesAsync(ct);
        }
        var sut = RequirementSut(factory, Auth(projectId, studentId), Profile("Supervisor"));
        var history = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(projectId, first.Id, ct));
        Assert.Equal(409, history.StatusCode);

        var second = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        await using (var db = factory.CreateDbContext())
        {
            db.SubmissionUploadSessions.Add(Session(projectId, second.Id, studentId, 1, Now.AddMinutes(10), active: true));
            await db.SaveChangesAsync(ct);
        }
        var active = await Assert.ThrowsAsync<ApiException>(() => sut.DeleteAsync(projectId, second.Id, ct));
        Assert.Equal(409, active.StatusCode);
    }

    [Fact]
    public async Task Requirement_StatusMachine_RejectsInvalidTransitionsAndArchivedMutation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var open = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var sut = RequirementSut(factory, Auth(projectId, studentId), Profile("Supervisor"));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.ReopenAsync(projectId, open.Id, ct))).StatusCode);
        await sut.CloseAsync(projectId, open.Id, ct);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CloseAsync(projectId, open.Id, ct))).StatusCode);
        await sut.ArchiveAsync(projectId, open.Id, ct);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.ReopenAsync(projectId, open.Id, ct))).StatusCode);
    }

    [Fact]
    public async Task Requirement_List_OrdersOpenClosedArchivedThenDueDateAndTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        await SeedRequirement(factory, projectId, studentId, Now.AddDays(3), ct, "Zulu", SubmissionConstants.RequirementStatus.Open);
        await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct, "Alpha", SubmissionConstants.RequirementStatus.Open);
        await SeedRequirement(factory, projectId, studentId, null, ct, "Closed", SubmissionConstants.RequirementStatus.Closed);
        await SeedRequirement(factory, projectId, studentId, null, ct, "Archived", SubmissionConstants.RequirementStatus.Archived);
        var sut = RequirementSut(factory, Auth(projectId, studentId), Profile("Supervisor"));
        var list = await sut.ListAsync(projectId, ct);
        Assert.Equal(new[] { "Alpha", "Zulu", "Closed", "Archived" }, list.Select(x => x.Title).ToArray());
    }

    [Fact]
    public async Task CreateUploadSession_RejectsClosedRequirementAndExistingNonRevisionSubmission()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var closed = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct, status: SubmissionConstants.RequirementStatus.Closed);
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), Substitute.For<IObjectStorageService>());
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(projectId, closed.Id, studentId, ValidUpload(), ct))).StatusCode);

        var open = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        await using (var db = factory.CreateDbContext())
        {
            db.ResearchSubmissions.Add(new ResearchSubmission { Id = Guid.NewGuid(), ProjectId = projectId, RequirementId = open.Id, Status = SubmissionConstants.SubmissionStatus.PendingReview, CurrentVersionId = Guid.NewGuid(), VersionCount = 1, LastSubmittedAt = Now, CreatedAt = Now });
            await db.SaveChangesAsync(ct);
        }
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(projectId, open.Id, studentId, ValidUpload(), ct))).StatusCode);
    }

    [Fact]
    public async Task CreateUploadSession_ActiveSameUserConflicts_ExpiredSessionIsReplacedAndCleaned()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct).Returns(new ObjectUploadGrant("https://upload", Now.AddMinutes(5), new Dictionary<string,string>()));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        var active = Session(projectId, req.Id, studentId, 1, Now.AddMinutes(5), active: true);
        await SeedSession(factory, active, ct);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CreateUploadSessionAsync(projectId, req.Id, studentId, ValidUpload(), ct))).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var row = await db.SubmissionUploadSessions.SingleAsync(x => x.Id == active.Id, ct);
            row.ExpiresAt = Now;
            await db.SaveChangesAsync(ct);
        }
        var replacement = await sut.CreateUploadSessionAsync(projectId, req.Id, studentId, ValidUpload(), ct);
        Assert.Equal(1, replacement.VersionNumber);
        await storage.Received(1).DeleteIfExistsAsync(active.TemporaryObjectKey, Arg.Any<CancellationToken>());
        await using var verify = factory.CreateDbContext();
        var stale = await verify.SubmissionUploadSessions.SingleAsync(x => x.Id == active.Id, ct);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Expired, stale.Status);
        Assert.Null(stale.ActiveSlot);
    }

    [Theory]
    [InlineData("report.txt", "text/plain", 100, null)]
    [InlineData("report.pdf", "text/plain", 100, null)]
    [InlineData("report.pdf", "application/pdf", 0, null)]
    [InlineData("report.pdf", "application/pdf", 20000000, null)]
    public async Task CreateUploadSession_InvalidFileCharacteristicsAreRejected(string fileName, string contentType, long size, string? note)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var storage = Substitute.For<IObjectStorageService>();
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);
        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateUploadSessionAsync(projectId, req.Id, studentId, new(fileName, contentType, size, note), ct));
        await storage.DidNotReceive().CreateUploadGrantAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUploadSession_UsesEarlierGrantExpiryAndMarksFailedWhenGrantCreationFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct).Returns(new ObjectUploadGrant("https://upload", Now.AddMinutes(2), new Dictionary<string,string>()));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);
        var response = await sut.CreateUploadSessionAsync(projectId, req.Id, studentId, ValidUpload(), ct);
        Assert.Equal(Now.AddMinutes(2), response.ExpiresAt);

        var req2 = await SeedRequirement(factory, projectId, studentId, Now.AddDays(2), ct, "Second");
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct)
            .Returns(Task.FromException<ObjectUploadGrant>(new InvalidOperationException("storage down")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateUploadSessionAsync(projectId, req2.Id, studentId, ValidUpload(), ct));
        await using var db = factory.CreateDbContext();
        var failed = await db.SubmissionUploadSessions.SingleAsync(x => x.RequirementId == req2.Id, ct);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Failed, failed.Status);
        Assert.Null(failed.ActiveSlot);
    }

    [Fact]
    public async Task CompleteUploadSession_EnforcesOwnerStatusExpiryAndRequirementOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var storage = Substitute.For<IObjectStorageService>();
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        var wrongOwner = Session(projectId, req.Id, studentId, 1, Now.AddMinutes(5), active:true); await SeedSession(factory, wrongOwner, ct);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(projectId, wrongOwner.Id, Guid.NewGuid(), ct))).StatusCode);

        await using (var db = factory.CreateDbContext()) { var row=await db.SubmissionUploadSessions.SingleAsync(x=>x.Id==wrongOwner.Id,ct); row.Status=SubmissionConstants.UploadSessionStatus.Failed; row.ActiveSlot=null; await db.SaveChangesAsync(ct); }
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(projectId, wrongOwner.Id, studentId, ct))).StatusCode);

        var expired = Session(projectId, req.Id, studentId, 1, Now, active:true); await SeedSession(factory, expired, ct);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(projectId, expired.Id, studentId, ct))).StatusCode);
        await storage.Received().DeleteIfExistsAsync(expired.TemporaryObjectKey, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, "application/pdf")]
    [InlineData(2048, "application/pdf")]
    [InlineData(1024, "text/plain")]
    public async Task CompleteUploadSession_InvalidStoredMetadataNeverPromotes(long length, string contentType)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId=Guid.NewGuid(); var studentId=Guid.NewGuid(); var req=await SeedRequirement(factory,projectId,studentId,Now.AddDays(1),ct);
        var session=Session(projectId,req.Id,studentId,1,Now.AddMinutes(5),active:true); await SeedSession(factory,session,ct);
        var storage=Substitute.For<IObjectStorageService>(); storage.GetMetadataAsync(session.TemporaryObjectKey,ct).Returns(new StoredObjectMetadata(length,contentType,"e"));
        var sut=SubmissionSut(factory,Auth(projectId,studentId),Profile("Student"),storage);
        await Assert.ThrowsAsync<ApiValidationException>(()=>sut.CompleteUploadSessionAsync(projectId,session.Id,studentId,ct));
        await storage.DidNotReceive().PromoteAsync(Arg.Any<string>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadSession_ValidFirstVersionPersistsExactSnapshotsAndLateBoundary()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var factory=await SqliteFactory.CreateAsync(ct);
        var projectId=Guid.NewGuid(); var studentId=Guid.NewGuid(); var req=await SeedRequirement(factory,projectId,studentId,Now,ct);
        var session=Session(projectId,req.Id,studentId,1,Now.AddMinutes(5),active:true); await SeedSession(factory,session,ct);
        var storage=Substitute.For<IObjectStorageService>();
        storage.GetMetadataAsync(session.TemporaryObjectKey,ct).Returns(new StoredObjectMetadata(1024,"application/pdf","temp"));
        storage.GetMetadataAsync(session.FinalObjectKey,ct).Returns(new StoredObjectMetadata(1024,"application/pdf","final"));
        var sut=SubmissionSut(factory,Auth(projectId,studentId),Profile("Student"),storage);
        var response=await sut.CompleteUploadSessionAsync(projectId,session.Id,studentId,ct);
        Assert.Equal(SubmissionConstants.SubmissionStatus.PendingReview,response.Status);
        Assert.Equal(1,response.VersionCount);
        var version=Assert.Single(response.Versions);
        Assert.False(version.IsLate);
        Assert.Equal("PROJECT_LEADER",version.SubmitterRoleSnapshot);
        Assert.Equal(SubmissionConstants.ResponsibilityMode.ProjectLeader,version.ResponsibilityModeSnapshot);
        Assert.True(version.IsCurrent);
        Assert.Equal("final", (await factory.CreateDbContext().SubmissionVersions.SingleAsync(x=>x.Id==session.VersionId,ct)).ObjectETag);
        await storage.Received(1).PromoteAsync(session.TemporaryObjectKey,session.FinalObjectKey,ct);
    }

    [Fact]
    public async Task Review_ValidationAndStateGuardsAreIndependent()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var factory=await SqliteFactory.CreateAsync(ct);
        var projectId=Guid.NewGuid(); var studentId=Guid.NewGuid(); var seeded=await SeedCompleted(factory,projectId,studentId,ct);
        var auth=Auth(projectId,studentId); var sut=SubmissionSut(factory,auth,Profile("Supervisor"),Substitute.For<IObjectStorageService>());
        await Assert.ThrowsAsync<ApiValidationException>(()=>sut.ReviewAsync(projectId,seeded.SubmissionId,Guid.NewGuid(),new(Guid.Empty,"BAD",null),ct));
        await Assert.ThrowsAsync<ApiValidationException>(()=>sut.ReviewAsync(projectId,seeded.SubmissionId,Guid.NewGuid(),new(seeded.VersionId,SubmissionConstants.ReviewDecision.ChangesRequested," "),ct));

        await using(var db=factory.CreateDbContext()){var s=await db.ResearchSubmissions.SingleAsync(x=>x.Id==seeded.SubmissionId,ct);s.Status=SubmissionConstants.SubmissionStatus.Approved;await db.SaveChangesAsync(ct);}        
        Assert.Equal(409,(await Assert.ThrowsAsync<ApiException>(()=>sut.ReviewAsync(projectId,seeded.SubmissionId,Guid.NewGuid(),new(seeded.VersionId,SubmissionConstants.ReviewDecision.Approved,null),ct))).StatusCode);
    }

    [Fact]
    public async Task Review_RejectedPersistsTrimmedFeedbackAndClearsApprovalFields()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var factory=await SqliteFactory.CreateAsync(ct);
        var projectId=Guid.NewGuid(); var studentId=Guid.NewGuid(); var seeded=await SeedCompleted(factory,projectId,studentId,ct);
        var profile=Profile("Supervisor Name"); var sut=SubmissionSut(factory,Auth(projectId,studentId),profile,Substitute.For<IObjectStorageService>());
        var response=await sut.ReviewAsync(projectId,seeded.SubmissionId,Guid.NewGuid(),new(seeded.VersionId," rejected ","  insufficient evidence  "),ct);
        Assert.Equal(SubmissionConstants.SubmissionStatus.Rejected,response.Status);
        Assert.Null(response.ApprovedVersionId); Assert.Null(response.ApprovedAt);
        var review=Assert.Single(response.Versions).Review;
        Assert.NotNull(review); Assert.Equal(SubmissionConstants.ReviewDecision.Rejected,review!.Decision); Assert.Equal("insufficient evidence",review.Feedback);
    }

    [Fact]
    public async Task Download_RequiresMatchingSubmissionAndVersionBeforeStorageGrant()
    {
        var ct=TestContext.Current.CancellationToken;
        await using var factory=await SqliteFactory.CreateAsync(ct);
        var projectId=Guid.NewGuid(); var studentId=Guid.NewGuid(); var seeded=await SeedCompleted(factory,projectId,studentId,ct);
        var storage=Substitute.For<IObjectStorageService>(); var auth=Auth(projectId,studentId); var sut=SubmissionSut(factory,auth,Profile("x"),storage);
        Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>sut.GetDownloadUrlAsync(projectId,Guid.NewGuid(),seeded.VersionId,true,ct))).StatusCode);
        Assert.Equal(404,(await Assert.ThrowsAsync<ApiException>(()=>sut.GetDownloadUrlAsync(projectId,seeded.SubmissionId,Guid.NewGuid(),true,ct))).StatusCode);
        await storage.DidNotReceive().CreateDownloadGrantAsync(Arg.Any<string>(),Arg.Any<string>(),Arg.Any<bool>(),Arg.Any<CancellationToken>());
    }


    [Fact]
    public async Task CreateUploadSession_RevisionRequiresFormalChangeRequestAndUsesNextVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        await using (var db = factory.CreateDbContext())
        {
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            submission.Status = SubmissionConstants.SubmissionStatus.ChangesRequested;
            db.SubmissionReviews.Add(new SubmissionReview
            {
                Id = Guid.NewGuid(), SubmissionId = seeded.SubmissionId, VersionId = seeded.VersionId,
                Decision = SubmissionConstants.ReviewDecision.ChangesRequested, Feedback = "Revise",
                ReviewedBy = Guid.NewGuid(), ReviewedByName = "Supervisor", ReviewedAt = Now
            });
            await db.SaveChangesAsync(ct);
        }

        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct)
            .Returns(new ObjectUploadGrant("https://upload", Now.AddMinutes(5), new Dictionary<string,string>()));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);
        var response = await sut.CreateUploadSessionAsync(projectId,
            (await factory.CreateDbContext().ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct)).RequirementId,
            studentId, ValidUpload(), ct);

        Assert.Equal(2, response.VersionNumber);
        await using var verify = factory.CreateDbContext();
        var session = await verify.SubmissionUploadSessions.SingleAsync(x => x.Id == response.UploadSessionId, ct);
        Assert.Equal(seeded.SubmissionId, session.SubmissionId);
        Assert.Equal(2, session.ExpectedVersionNumber);
        Assert.Equal("ACTIVE", session.ActiveSlot);
    }

    [Fact]
    public async Task CreateUploadSession_DifferentOwnerActiveSessionIsFailedAndReplaced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddDays(1), ct);
        var stale = Session(projectId, req.Id, Guid.NewGuid(), 1, Now.AddMinutes(5), active: true);
        await SeedSession(factory, stale, ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateUploadGrantAsync(Arg.Any<string>(), "application/pdf", ct)
            .Returns(new ObjectUploadGrant("https://upload", Now.AddMinutes(5), new Dictionary<string,string>()));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        var replacement = await sut.CreateUploadSessionAsync(projectId, req.Id, studentId, ValidUpload(), ct);

        Assert.Equal(1, replacement.VersionNumber);
        await using var verify = factory.CreateDbContext();
        var old = await verify.SubmissionUploadSessions.SingleAsync(x => x.Id == stale.Id, ct);
        Assert.Equal(SubmissionConstants.UploadSessionStatus.Failed, old.Status);
        Assert.Null(old.ActiveSlot);
        Assert.Equal("Upload session was replaced after submission responsibility changed.", old.FailureReason);
        await storage.Received(1).DeleteIfExistsAsync(stale.TemporaryObjectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadSession_RevisionRejectsChangedSubmissionAndVersionRaceBeforeStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        Guid requirementId;
        await using (var db = factory.CreateDbContext())
        {
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            requirementId = submission.RequirementId;
            submission.Status = SubmissionConstants.SubmissionStatus.ChangesRequested;
            db.SubmissionReviews.Add(new SubmissionReview { Id = Guid.NewGuid(), SubmissionId = seeded.SubmissionId, VersionId = seeded.VersionId, Decision = SubmissionConstants.ReviewDecision.ChangesRequested, Feedback = "Revise", ReviewedBy = Guid.NewGuid(), ReviewedByName = "Supervisor", ReviewedAt = Now });
            await db.SaveChangesAsync(ct);
        }
        var wrongSubmission = Session(projectId, requirementId, studentId, 2, Now.AddMinutes(5), active: true);
        await SeedSession(factory, wrongSubmission, ct);
        var storage = Substitute.For<IObjectStorageService>();
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(projectId, wrongSubmission.Id, studentId, ct))).StatusCode);
        await storage.DidNotReceive().GetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using (var db = factory.CreateDbContext())
        {
            db.SubmissionUploadSessions.Remove(await db.SubmissionUploadSessions.SingleAsync(x => x.Id == wrongSubmission.Id, ct));
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            submission.VersionCount = 2;
            await db.SaveChangesAsync(ct);
        }
        var raced = Session(projectId, requirementId, studentId, 2, Now.AddMinutes(5), active: true);
        raced.SubmissionId = seeded.SubmissionId;
        await SeedSession(factory, raced, ct);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.CompleteUploadSessionAsync(projectId, raced.Id, studentId, ct))).StatusCode);
        await storage.DidNotReceive().GetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadSession_RevisionSuccessCreatesVersionTwoAndClearsPriorApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        Guid requirementId;
        await using (var db = factory.CreateDbContext())
        {
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            requirementId = submission.RequirementId;
            submission.Status = SubmissionConstants.SubmissionStatus.ChangesRequested;
            submission.ApprovedVersionId = seeded.VersionId;
            submission.ApprovedAt = Now.AddHours(-1);
            db.SubmissionReviews.Add(new SubmissionReview { Id = Guid.NewGuid(), SubmissionId = seeded.SubmissionId, VersionId = seeded.VersionId, Decision = SubmissionConstants.ReviewDecision.ChangesRequested, Feedback = "Revise", ReviewedBy = Guid.NewGuid(), ReviewedByName = "Supervisor", ReviewedAt = Now.AddMinutes(-5) });
            await db.SaveChangesAsync(ct);
        }
        var session = Session(projectId, requirementId, studentId, 2, Now.AddMinutes(5), active: true);
        session.SubmissionId = seeded.SubmissionId;
        session.SubmissionNote = "revision";
        await SeedSession(factory, session, ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.GetMetadataAsync(session.TemporaryObjectKey, ct).Returns(new StoredObjectMetadata(1024, "application/pdf", "temp-v2"));
        storage.GetMetadataAsync(session.FinalObjectKey, ct).Returns(new StoredObjectMetadata(1024, "application/pdf", "final-v2"));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        var response = await sut.CompleteUploadSessionAsync(projectId, session.Id, studentId, ct);

        Assert.Equal(SubmissionConstants.SubmissionStatus.PendingReview, response.Status);
        Assert.Equal(2, response.VersionCount);
        Assert.Equal(session.VersionId, response.CurrentVersionId);
        Assert.Null(response.ApprovedVersionId);
        Assert.Null(response.ApprovedAt);
        var current = Assert.Single(response.Versions, x => x.IsCurrent);
        Assert.Equal(2, current.VersionNumber);
        Assert.Equal("revision", current.SubmissionNote);
        Assert.False(current.IsApproved);
        await storage.Received(1).PromoteAsync(session.TemporaryObjectKey, session.FinalObjectKey, ct);
        await storage.Received(1).DeleteIfExistsAsync(session.TemporaryObjectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteUploadSession_AfterDueDateMarksLateAndUsesTemporaryMetadataWhenFinalLookupIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var req = await SeedRequirement(factory, projectId, studentId, Now.AddTicks(-1), ct);
        var session = Session(projectId, req.Id, studentId, 1, Now.AddMinutes(5), active: true);
        await SeedSession(factory, session, ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.GetMetadataAsync(session.TemporaryObjectKey, ct).Returns(new StoredObjectMetadata(1024, "application/pdf", "temporary-etag"));
        storage.GetMetadataAsync(session.FinalObjectKey, ct).Returns((StoredObjectMetadata?)null);
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Student"), storage);

        var response = await sut.CompleteUploadSessionAsync(projectId, session.Id, studentId, ct);

        var version = Assert.Single(response.Versions);
        Assert.True(version.IsLate);
        await using var verify = factory.CreateDbContext();
        var stored = await verify.SubmissionVersions.SingleAsync(x => x.Id == session.VersionId, ct);
        Assert.Equal(1024, stored.FileSizeBytes);
        Assert.Equal("temporary-etag", stored.ObjectETag);
    }

    [Fact]
    public async Task Review_ApprovalPersistsExactReviewerAndApprovalSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid(); var reviewerId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Supervisor Exact"), Substitute.For<IObjectStorageService>());

        var response = await sut.ReviewAsync(projectId, seeded.SubmissionId, reviewerId,
            new(seeded.VersionId, " approved ", "   "), ct);

        Assert.Equal(SubmissionConstants.SubmissionStatus.Approved, response.Status);
        Assert.Equal(seeded.VersionId, response.ApprovedVersionId);
        Assert.Equal(Now, response.ApprovedAt);
        var review = Assert.Single(response.Versions).Review;
        Assert.NotNull(review);
        Assert.Equal(reviewerId, review!.ReviewedBy);
        Assert.Equal("Supervisor Exact", review.ReviewedByName);
        Assert.Equal(Now, review.ReviewedAt);
        Assert.Null(review.Feedback);
    }

    [Fact]
    public async Task Review_StaleCurrentVersionMissingVersionAndDuplicateReviewAreDistinctConflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("Supervisor"), Substitute.For<IObjectStorageService>());

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.ReviewAsync(projectId, seeded.SubmissionId, Guid.NewGuid(), new(Guid.NewGuid(), SubmissionConstants.ReviewDecision.Approved, null), ct))).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            var missing = Guid.NewGuid();
            submission.CurrentVersionId = missing;
            await db.SaveChangesAsync(ct);
        }
        var currentMissing = (await factory.CreateDbContext().ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct)).CurrentVersionId!.Value;
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => sut.ReviewAsync(projectId, seeded.SubmissionId, Guid.NewGuid(), new(currentMissing, SubmissionConstants.ReviewDecision.Approved, null), ct))).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var submission = await db.ResearchSubmissions.SingleAsync(x => x.Id == seeded.SubmissionId, ct);
            submission.CurrentVersionId = seeded.VersionId;
            db.SubmissionReviews.Add(new SubmissionReview { Id = Guid.NewGuid(), SubmissionId = seeded.SubmissionId, VersionId = seeded.VersionId, Decision = SubmissionConstants.ReviewDecision.Approved, ReviewedBy = Guid.NewGuid(), ReviewedByName = "Existing", ReviewedAt = Now });
            await db.SaveChangesAsync(ct);
        }
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => sut.ReviewAsync(projectId, seeded.SubmissionId, Guid.NewGuid(), new(seeded.VersionId, SubmissionConstants.ReviewDecision.Approved, null), ct))).StatusCode);
    }

    [Fact]
    public async Task Download_ForwardsStoredObjectFileNameAndInlineFlagExactly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await SqliteFactory.CreateAsync(ct);
        var projectId = Guid.NewGuid(); var studentId = Guid.NewGuid();
        var seeded = await SeedCompleted(factory, projectId, studentId, ct);
        var storage = Substitute.For<IObjectStorageService>();
        storage.CreateDownloadGrantAsync("projects/final/report.pdf", "report.pdf", false, ct)
            .Returns(new ObjectDownloadGrant("https://download", Now.AddMinutes(3)));
        var sut = SubmissionSut(factory, Auth(projectId, studentId), Profile("x"), storage);

        var response = await sut.GetDownloadUrlAsync(projectId, seeded.SubmissionId, seeded.VersionId, false, ct);

        Assert.Equal("https://download", response.Url);
        Assert.Equal(Now.AddMinutes(3), response.ExpiresAt);
        await storage.Received(1).CreateDownloadGrantAsync("projects/final/report.pdf", "report.pdf", false, ct);
    }

    private static SubmissionRequirementService RequirementSut(IDbContextFactory<SubmissionDbContext> factory,IProjectAuthorizationClient auth,IUserProfileClient profile)=>new(factory,auth,profile,new SubmissionOptions{AllowedFileTypes=["pdf","docx","pptx","zip"]},new StorageOptions{MaximumFileSizeBytes=MaxBytes},new FixedTimeProvider(Now));
    private static ResearchSubmissionService SubmissionSut(IDbContextFactory<SubmissionDbContext> factory,IProjectAuthorizationClient auth,IUserProfileClient profile,IObjectStorageService storage)=>new(factory,auth,profile,storage,new SubmissionOptions{AllowedFileTypes=["pdf","docx","pptx","zip"],UploadSessionLifetimeMinutes=10},new FixedTimeProvider(Now),NullLogger<ResearchSubmissionService>.Instance);
    private static IUserProfileClient Profile(string name){var p=Substitute.For<IUserProfileClient>();p.GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>()).Returns(name);return p;}
    private static IProjectAuthorizationClient Auth(Guid projectId,Guid studentId){var a=Substitute.For<IProjectAuthorizationClient>();a.GetSubmissionContextAsync(projectId,Arg.Any<CancellationToken>()).Returns(new ProjectSubmissionContext(new ProjectStudentContext(studentId,"Student","s@example.edu","IT001"),[new ProjectStudentContext(studentId,"Student","s@example.edu","IT001")]));return a;}
    private static CreateUploadSessionRequest ValidUpload()=>new("report.pdf","application/pdf",1024," note ");

    private static async Task<SubmissionRequirement> SeedRequirement(IDbContextFactory<SubmissionDbContext> factory,Guid projectId,Guid studentId,DateTimeOffset? dueAt,CancellationToken ct,string title="Final report",string status=SubmissionConstants.RequirementStatus.Open){var r=new SubmissionRequirement{Id=Guid.NewGuid(),ProjectId=projectId,Title=title,Description="D",DueAt=dueAt,AllowedFileTypes="pdf,docx",MaxFileSizeBytes=MaxBytes,Status=status,ResponsibilityMode=SubmissionConstants.ResponsibilityMode.ProjectLeader,AssignedStudentId=studentId,AssignedStudentName="Student",CreatedBy=Guid.NewGuid(),CreatedByName="Supervisor",CreatedAt=Now};await using var db=await factory.CreateDbContextAsync(ct);db.SubmissionRequirements.Add(r);await db.SaveChangesAsync(ct);return r;}
    private static SubmissionUploadSession Session(Guid projectId,Guid requirementId,Guid userId,int version,DateTimeOffset expiresAt,bool active){var id=Guid.NewGuid();return new SubmissionUploadSession{Id=id,ProjectId=projectId,RequirementId=requirementId,SubmissionId=Guid.NewGuid(),VersionId=Guid.NewGuid(),ExpectedVersionNumber=version,TemporaryObjectKey=$"pending/{id:N}",FinalObjectKey=$"final/{id:N}",OriginalFileName="report.pdf",FileExtension="pdf",ExpectedContentType="application/pdf",DeclaredFileSizeBytes=1024,ExpectedMaxFileSizeBytes=MaxBytes,SubmissionNote="note",CreatedBy=userId,CreatedByName="Student",Status=SubmissionConstants.UploadSessionStatus.Pending,ActiveSlot=active?"ACTIVE":null,ExpiresAt=expiresAt,CreatedAt=Now};}
    private static async Task SeedSession(IDbContextFactory<SubmissionDbContext> factory,SubmissionUploadSession session,CancellationToken ct){await using var db=await factory.CreateDbContextAsync(ct);db.SubmissionUploadSessions.Add(session);await db.SaveChangesAsync(ct);}
    private static async Task<(Guid SubmissionId,Guid VersionId)> SeedCompleted(IDbContextFactory<SubmissionDbContext> factory,Guid projectId,Guid studentId,CancellationToken ct){var req=await SeedRequirement(factory,projectId,studentId,Now.AddDays(1),ct);var sid=Guid.NewGuid();var vid=Guid.NewGuid();await using var db=await factory.CreateDbContextAsync(ct);db.ResearchSubmissions.Add(new ResearchSubmission{Id=sid,ProjectId=projectId,RequirementId=req.Id,Status=SubmissionConstants.SubmissionStatus.PendingReview,CurrentVersionId=vid,VersionCount=1,LastSubmittedAt=Now,CreatedAt=Now});db.SubmissionVersions.Add(new SubmissionVersion{Id=vid,SubmissionId=sid,VersionNumber=1,ObjectKey="projects/final/report.pdf",OriginalFileName="report.pdf",FileExtension="pdf",ContentType="application/pdf",FileSizeBytes=1024,UploadedBy=studentId,UploadedByName="Student",SubmitterRoleSnapshot="PROJECT_LEADER",ResponsibilityModeSnapshot=SubmissionConstants.ResponsibilityMode.ProjectLeader,SubmittedAt=Now});await db.SaveChangesAsync(ct);return(sid,vid);}

    private sealed class FixedTimeProvider(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class SqliteFactory:IDbContextFactory<SubmissionDbContext>,IAsyncDisposable
    {
        private readonly SqliteConnection _connection; private readonly DbContextOptions<SubmissionDbContext> _options;
        private SqliteFactory(SqliteConnection connection){_connection=connection;_options=new DbContextOptionsBuilder<SubmissionDbContext>().UseSqlite(connection).Options;}
        public static async Task<SqliteFactory>CreateAsync(CancellationToken ct){var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync(ct);var f=new SqliteFactory(c);await using var db=f.CreateDbContext();await db.Database.EnsureCreatedAsync(ct);return f;}
        public SubmissionDbContext CreateDbContext()=>new(_options); public Task<SubmissionDbContext>CreateDbContextAsync(CancellationToken cancellationToken=default)=>Task.FromResult(CreateDbContext()); public ValueTask DisposeAsync()=>_connection.DisposeAsync();
    }
}
