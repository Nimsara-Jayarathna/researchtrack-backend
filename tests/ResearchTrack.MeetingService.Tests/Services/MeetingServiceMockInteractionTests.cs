using NSubstitute;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Contracts;
using ResearchTrack.MeetingService.Domain;
using ResearchTrack.MeetingService.Features;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Tests.Services;

public sealed class MeetingServiceMockInteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChannelCreate_Supervisor_VerifiesManageAndPersistsOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(cancellationToken).Returns("Ada Supervisor");
        var sut = new MeetingChannelService(repository, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await sut.CreateAsync(
            projectId,
            Guid.NewGuid(),
            AuthSecurityConstants.Roles.Supervisor,
            new MeetingChannelCreateRequest("ZOOM", "Weekly", "https://example.test/meeting"),
            cancellationToken);

        await auth.Received(1).EnsureCanManageAsync(projectId, cancellationToken);
        await auth.DidNotReceive().EnsureCanAccessAsync(projectId, cancellationToken);
        await repository.Received(1).AddAsync(
            Arg.Is<MeetingChannel>(x => x.ProjectId == projectId),
            cancellationToken);
    }

    [Fact]
    public async Task ChannelCreate_InvalidRole_PerformsNoAuthorizationProfileOrPersistenceCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        var sut = new MeetingChannelService(repository, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(
            projectId,
            Guid.NewGuid(),
            "ADMIN",
            new MeetingChannelCreateRequest("ZOOM", "Weekly", "https://example.test/meeting"),
            cancellationToken));

        await auth.DidNotReceive().EnsureCanManageAsync(Arg.Any<Guid>(), cancellationToken);
        await auth.DidNotReceive().EnsureCanAccessAsync(Arg.Any<Guid>(), cancellationToken);
        await profile.DidNotReceive().GetCurrentUserDisplayNameAsync(cancellationToken);
        await repository.DidNotReceive().AddAsync(Arg.Any<MeetingChannel>(), cancellationToken);
    }

    [Fact]
    public async Task RecordCreate_InvalidChannel_DoesNotPersistOrResolveProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var records = Substitute.For<IMeetingRecordRepository>();
        var channels = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        channels.FindAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), cancellationToken)
            .Returns((MeetingChannel?)null);
        var sut = new MeetingRecordService(records, channels, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(
            projectId,
            Guid.NewGuid(),
            AuthSecurityConstants.Roles.Student,
            new MeetingRecordUpsertRequest("2026-10-09", 30, "Discussed progress", null, Guid.NewGuid()),
            cancellationToken));

        await auth.Received(1).EnsureCanAccessAsync(projectId, cancellationToken);
        await profile.DidNotReceive().GetCurrentUserDisplayNameAsync(cancellationToken);
        await records.DidNotReceive().AddAsync(Arg.Any<MeetingRecord>(), cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
