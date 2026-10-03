using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Domain;
using ResearchTrack.MeetingService.Features;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Tests.Features;

public sealed class MeetingChannelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Supervisor_create_is_approved_and_uses_manage_authorization()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            projectId,
            supervisorId,
            AuthSecurityConstants.Roles.Supervisor,
            new MeetingChannelUpsertRequest(
                " zoom ",
                "  Weekly supervision  ",
                "  https://example.test/room  "),
            TestContext.Current.CancellationToken);

        Assert.Equal("ZOOM", result.Platform);
        Assert.Equal("Weekly supervision", result.ChannelName);
        Assert.Equal("https://example.test/room", result.LinkOrIdentifier);
        Assert.Equal(MeetingChannelConstants.StatusApproved, result.Status);
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
        Assert.Single(fixture.Repository.Items);
    }

    [Fact]
    public async Task Student_create_is_pending_and_uses_access_authorization()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            projectId,
            studentId,
            AuthSecurityConstants.Roles.Student,
            new MeetingChannelUpsertRequest(
                "TEAMS",
                "Student proposal",
                "https://teams.example.test/meeting"),
            TestContext.Current.CancellationToken);

        Assert.Equal(MeetingChannelConstants.StatusPending, result.Status);
        Assert.Equal(AuthSecurityConstants.Roles.Student, result.AddedByRole);
        Assert.Equal(studentId, result.AddedBy);
        Assert.Null(result.ApprovedBy);
        Assert.Null(result.ApprovedByName);
        Assert.Null(result.ApprovedAt);
        Assert.Equal(0, fixture.Authorization.ManageChecks);
        Assert.Equal(1, fixture.Authorization.AccessChecks);
    }

    [Fact]
    public async Task Create_rejects_non_http_link_without_persisting()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ApiValidationException>(async () =>
            await fixture.Service.CreateAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                AuthSecurityConstants.Roles.Supervisor,
                new MeetingChannelUpsertRequest(
                    "ZOOM",
                    "Weekly supervision",
                    "javascript:alert(1)"),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            exception.FieldErrors,
            error => error.Field == "linkOrIdentifier");
        Assert.Empty(fixture.Repository.Items);
        Assert.Equal(0, fixture.Profile.Calls);
    }

    [Fact]
    public async Task Update_preserves_approval_metadata_and_sets_updated_time()
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

        fixture.Time.UtcNow = Now.AddHours(1);

        var updated = await fixture.Service.UpdateAsync(
            projectId,
            created.Id,
            new MeetingChannelUpsertRequest(
                "GOOGLE_MEET",
                "Updated channel",
                "https://meet.example.test/updated"),
            TestContext.Current.CancellationToken);

        Assert.Equal("GOOGLE_MEET", updated.Platform);
        Assert.Equal("Updated channel", updated.ChannelName);
        Assert.Equal(created.ApprovedBy, updated.ApprovedBy);
        Assert.Equal(created.ApprovedAt, updated.ApprovedAt);
        Assert.Equal(Now.AddHours(1), updated.UpdatedAt);
    }

    [Fact]
    public async Task Approve_transitions_pending_student_channel_once()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();

        var pending = await fixture.Service.CreateAsync(
            projectId,
            studentId,
            AuthSecurityConstants.Roles.Student,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        fixture.Time.UtcNow = Now.AddMinutes(10);
        fixture.Profile.DisplayName = "Grace Supervisor";

        var approved = await fixture.Service.ApproveAsync(
            projectId,
            pending.Id,
            supervisorId,
            TestContext.Current.CancellationToken);

        Assert.Equal(MeetingChannelConstants.StatusApproved, approved.Status);
        Assert.Equal(supervisorId, approved.ApprovedBy);
        Assert.Equal("Grace Supervisor", approved.ApprovedByName);
        Assert.Equal(Now.AddMinutes(10), approved.ApprovedAt);
        Assert.Equal(Now.AddMinutes(10), approved.UpdatedAt);

        var exception = await Assert.ThrowsAsync<ApiValidationException>(async () =>
            await fixture.Service.ApproveAsync(
                projectId,
                pending.Id,
                supervisorId,
                TestContext.Current.CancellationToken));

        Assert.Contains(exception.FieldErrors, error => error.Field == "status");
    }

    [Fact]
    public async Task List_is_project_authorized_and_returns_repository_order()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var student = MeetingChannel.CreateStudent(
            projectId,
            "ZOOM",
            "Pending",
            "https://example.test/pending",
            Guid.NewGuid(),
            "Student",
            Now.AddMinutes(1));
        var supervisor = MeetingChannel.CreateSupervisor(
            projectId,
            "TEAMS",
            "Approved",
            "https://example.test/approved",
            Guid.NewGuid(),
            "Supervisor",
            Now);
        fixture.Repository.Items.Add(student);
        fixture.Repository.Items.Add(supervisor);

        var result = await fixture.Service.ListAsync(
            projectId,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(student.Id, result[0].Id);
        Assert.Equal(supervisor.Id, result[1].Id);
        Assert.Equal(1, fixture.Authorization.AccessChecks);
    }

    [Fact]
    public async Task Delete_requires_manage_access_and_removes_project_channel()
    {
        var fixture = new Fixture();
        var projectId = Guid.NewGuid();
        var channel = MeetingChannel.CreateSupervisor(
            projectId,
            "ZOOM",
            "Weekly",
            "https://example.test/weekly",
            Guid.NewGuid(),
            "Supervisor",
            Now);
        fixture.Repository.Items.Add(channel);

        await fixture.Service.DeleteAsync(
            projectId,
            channel.Id,
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Repository.Items);
        Assert.Equal(1, fixture.Authorization.ManageChecks);
    }

    private static MeetingChannelUpsertRequest ValidRequest() =>
        new("ZOOM", "Weekly supervision", "https://example.test/weekly");

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository = new FakeRepository();
            Authorization = new FakeAuthorizationClient();
            Profile = new FakeUserProfileClient();
            Time = new MutableTimeProvider { UtcNow = Now };
            Service = new MeetingChannelService(
                Repository,
                Authorization,
                Profile,
                Time);
        }

        public FakeRepository Repository { get; }
        public FakeAuthorizationClient Authorization { get; }
        public FakeUserProfileClient Profile { get; }
        public MutableTimeProvider Time { get; }
        public MeetingChannelService Service { get; }
    }

    private sealed class FakeRepository : IMeetingChannelRepository
    {
        public List<MeetingChannel> Items { get; } = [];

        public Task<IReadOnlyList<MeetingChannel>> ListByProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MeetingChannel> result = Items
                .Where(channel => channel.ProjectId == projectId)
                .OrderBy(channel => channel.Status == MeetingChannelConstants.StatusPending ? 0 : 1)
                .ThenByDescending(channel => channel.CreatedAt)
                .ThenBy(channel => channel.Id)
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
