using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Domain;
using ResearchTrack.MeetingService.Features;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Tests.Features;

public sealed class MeetingRecordServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Supervisor_create_is_approved_and_uses_manage_authorization()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        var channel = AddSupervisorChannel(fixture, projectId);

        var result = await fixture.Service.CreateAsync(
            projectId,
            supervisorId,
            AuthSecurityConstants.Roles.Supervisor,
            ValidRequest(channel.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal("2026-10-02", result.MeetingDate);
        Assert.Equal(45, result.DurationMinutes);
        Assert.Equal("Discussed methodology", result.DiscussionSummary);
        Assert.Equal("Detailed notes", result.DiscussionDetails);
        Assert.Equal(channel.Id, result.ChannelId);
        Assert.Equal(MeetingRecordConstants.StatusApproved, result.Status);
        Assert.Equal(AuthSecurityConstants.Roles.Supervisor, result.AddedByRole);
        Assert.Equal(supervisorId, result.AddedBy);
        Assert.Equal(supervisorId, result.ApprovedBy);
        Assert.Equal("Ada Supervisor", result.AddedByName);
        Assert.Equal("Ada Supervisor", result.ApprovedByName);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Equal(Now, result.ApprovedAt);
        Assert.Null(result.UpdatedAt);
        Assert.Equal(1, fixture.Authorization.ManageChecks);
        Assert.Equal(0, fixture.Authorization.AccessChecks);
        Assert.Single(fixture.Records.Items);
    }

    [Fact]
    public async Task Student_create_is_pending_and_uses_access_authorization()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        fixture.Profile.DisplayName = "Student One";

        var result = await fixture.Service.CreateAsync(
            projectId,
            studentId,
            AuthSecurityConstants.Roles.Student,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(MeetingRecordConstants.StatusPending, result.Status);
        Assert.Equal(AuthSecurityConstants.Roles.Student, result.AddedByRole);
        Assert.Equal(studentId, result.AddedBy);
        Assert.Equal("Student One", result.AddedByName);
        Assert.Null(result.ApprovedBy);
        Assert.Null(result.ApprovedByName);
        Assert.Null(result.ApprovedAt);
        Assert.Equal(1, fixture.Authorization.AccessChecks);
        Assert.Equal(0, fixture.Authorization.ManageChecks);
    }

    [Fact]
    public async Task Create_rejects_invalid_date_duration_and_summary_without_persisting()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ApiValidationException>(async () =>
            await fixture.Service.CreateAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                AuthSecurityConstants.Roles.Supervisor,
                new MeetingRecordUpsertRequest(
                    "03/10/2026",
                    0,
                    "   ",
                    null,
                    null),
                TestContext.Current.CancellationToken));

        Assert.Contains(exception.FieldErrors, error => error.Field == "meetingDate");
        Assert.Contains(exception.FieldErrors, error => error.Field == "durationMinutes");
        Assert.Contains(exception.FieldErrors, error => error.Field == "discussionSummary");
        Assert.Empty(fixture.Records.Items);
        Assert.Equal(0, fixture.Profile.Calls);
    }

    [Fact]
    public async Task Create_rejects_channel_from_another_project()
    {
        var fixture = new Fixture();
        var requestedProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var channel = AddSupervisorChannel(fixture, otherProjectId);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(async () =>
            await fixture.Service.CreateAsync(
                requestedProjectId,
                Guid.NewGuid(),
                AuthSecurityConstants.Roles.Supervisor,
                ValidRequest(channel.Id),
                TestContext.Current.CancellationToken));

        Assert.Contains(exception.FieldErrors, error => error.Field == "channelId");
        Assert.Empty(fixture.Records.Items);
    }

    [Fact]
    public async Task Update_changes_editable_fields_but_preserves_creator_and_approval_metadata()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();
        var created = await fixture.Service.CreateAsync(
            projectId,
            supervisorId,
            AuthSecurityConstants.Roles.Supervisor,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        fixture.Time.UtcNow = Now.AddHours(2);
        var updated = await fixture.Service.UpdateAsync(
            projectId,
            created.Id,
            new MeetingRecordUpsertRequest(
                "2026-10-03",
                60,
                "Updated discussion",
                "Updated details",
                null),
            TestContext.Current.CancellationToken);

        Assert.Equal("2026-10-03", updated.MeetingDate);
        Assert.Equal(60, updated.DurationMinutes);
        Assert.Equal("Updated discussion", updated.DiscussionSummary);
        Assert.Equal("Updated details", updated.DiscussionDetails);
        Assert.Equal(created.AddedBy, updated.AddedBy);
        Assert.Equal(created.ApprovedBy, updated.ApprovedBy);
        Assert.Equal(created.ApprovedAt, updated.ApprovedAt);
        Assert.Equal(Now.AddHours(2), updated.UpdatedAt);
    }

    [Fact]
    public async Task Approve_transitions_pending_student_record_once()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var pending = await fixture.Service.CreateAsync(
            projectId,
            Guid.NewGuid(),
            AuthSecurityConstants.Roles.Student,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        fixture.Time.UtcNow = Now.AddMinutes(15);
        fixture.Profile.DisplayName = "Grace Supervisor";
        var supervisorId = Guid.NewGuid();

        var approved = await fixture.Service.ApproveAsync(
            projectId,
            pending.Id,
            supervisorId,
            TestContext.Current.CancellationToken);

        Assert.Equal(MeetingRecordConstants.StatusApproved, approved.Status);
        Assert.Equal(supervisorId, approved.ApprovedBy);
        Assert.Equal("Grace Supervisor", approved.ApprovedByName);
        Assert.Equal(Now.AddMinutes(15), approved.ApprovedAt);
        Assert.Equal(Now.AddMinutes(15), approved.UpdatedAt);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(async () =>
            await fixture.Service.ApproveAsync(
                projectId,
                pending.Id,
                supervisorId,
                TestContext.Current.CancellationToken));

        Assert.Contains(exception.FieldErrors, error => error.Field == "status");
    }

    [Fact]
    public async Task List_is_project_authorized_and_pending_first_then_meeting_date_descending()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var pending = MeetingRecord.CreateStudent(
            projectId,
            new DateTime(2026, 9, 1),
            30,
            "Pending",
            null,
            null,
            Guid.NewGuid(),
            "Student",
            Now.AddHours(-2));
        var recentApproved = MeetingRecord.CreateSupervisor(
            projectId,
            new DateTime(2026, 10, 2),
            45,
            "Recent",
            null,
            null,
            Guid.NewGuid(),
            "Supervisor",
            Now);
        var olderApproved = MeetingRecord.CreateSupervisor(
            projectId,
            new DateTime(2026, 9, 30),
            45,
            "Older",
            null,
            null,
            Guid.NewGuid(),
            "Supervisor",
            Now.AddHours(-1));
        fixture.Records.Items.AddRange([olderApproved, recentApproved, pending]);

        var result = await fixture.Service.ListAsync(
            projectId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { pending.Id, recentApproved.Id, olderApproved.Id },
            result.Select(x => x.Id).ToArray());
        Assert.Equal(1, fixture.Authorization.AccessChecks);
    }

    [Fact]
    public async Task Delete_requires_manage_access_and_removes_record()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var record = MeetingRecord.CreateSupervisor(
            projectId,
            new DateTime(2026, 10, 1),
            20,
            "Delete me",
            null,
            null,
            Guid.NewGuid(),
            "Supervisor",
            Now);
        fixture.Records.Items.Add(record);

        await fixture.Service.DeleteAsync(
            projectId,
            record.Id,
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Records.Items);
        Assert.Equal(1, fixture.Authorization.ManageChecks);
    }

    private static MeetingRecordUpsertRequest ValidRequest(Guid? channelId = null) =>
        new(
            "2026-10-02",
            45,
            "  Discussed methodology  ",
            "  Detailed notes  ",
            channelId);

    private static MeetingChannel AddSupervisorChannel(Fixture fixture, Guid projectId)
    {
        var channel = MeetingChannel.CreateSupervisor(
            projectId,
            "ZOOM",
            "Weekly",
            "https://example.test/weekly",
            Guid.NewGuid(),
            "Supervisor",
            Now);
        fixture.Channels.Items.Add(channel);
        return channel;
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Records = new FakeMeetingRecordRepository();
            Channels = new FakeMeetingChannelRepository();
            Authorization = new FakeAuthorizationClient();
            Profile = new FakeUserProfileClient();
            Time = new MutableTimeProvider { UtcNow = Now };
            Service = new MeetingRecordService(
                Records,
                Channels,
                Authorization,
                Profile,
                Time);
        }

        public FakeMeetingRecordRepository Records { get; }
        public FakeMeetingChannelRepository Channels { get; }
        public FakeAuthorizationClient Authorization { get; }
        public FakeUserProfileClient Profile { get; }
        public MutableTimeProvider Time { get; }
        public MeetingRecordService Service { get; }
    }

    private sealed class FakeMeetingRecordRepository : IMeetingRecordRepository
    {
        public List<MeetingRecord> Items { get; } = [];

        public Task<IReadOnlyList<MeetingRecord>> ListByProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MeetingRecord> result = Items
                .Where(record => record.ProjectId == projectId)
                .OrderBy(record => record.Status == MeetingRecordConstants.StatusPending ? 0 : 1)
                .ThenByDescending(record => record.MeetingDate)
                .ThenByDescending(record => record.CreatedAt)
                .ThenBy(record => record.Id)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<MeetingRecord?> FindAsync(
            Guid projectId,
            Guid recordId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.SingleOrDefault(
                    record => record.ProjectId == projectId && record.Id == recordId));

        public Task AddAsync(MeetingRecord record, CancellationToken cancellationToken)
        {
            Items.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveAsync(MeetingRecord record, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(MeetingRecord record, CancellationToken cancellationToken)
        {
            Items.Remove(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMeetingChannelRepository : IMeetingChannelRepository
    {
        public List<MeetingChannel> Items { get; } = [];

        public Task<IReadOnlyList<MeetingChannel>> ListByProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MeetingChannel> result = Items
                .Where(channel => channel.ProjectId == projectId)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<MeetingChannel?> FindAsync(
            Guid projectId,
            Guid channelId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.SingleOrDefault(
                    channel => channel.ProjectId == projectId && channel.Id == channelId));

        public Task AddAsync(MeetingChannel channel, CancellationToken cancellationToken)
        {
            Items.Add(channel);
            return Task.CompletedTask;
        }

        public Task SaveAsync(MeetingChannel channel, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(MeetingChannel channel, CancellationToken cancellationToken)
        {
            Items.Remove(channel);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthorizationClient : IProjectAuthorizationClient
    {
        public int AccessChecks { get; private set; }
        public int ManageChecks { get; private set; }

        public Task EnsureCanAccessAsync(Guid projectId, CancellationToken cancellationToken)
        {
            AccessChecks++;
            return Task.CompletedTask;
        }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            ManageChecks++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserProfileClient : IUserProfileClient
    {
        public string DisplayName { get; set; } = "Ada Supervisor";
        public int Calls { get; private set; }

        public Task<string> GetCurrentUserDisplayNameAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(DisplayName);
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
