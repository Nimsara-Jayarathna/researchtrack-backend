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
        var repository = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        profile.GetCurrentUserDisplayNameAsync(Arg.Any<CancellationToken>()).Returns("Ada Supervisor");
        var sut = new MeetingChannelService(repository, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await sut.CreateAsync(projectId, Guid.NewGuid(), AuthSecurityConstants.Roles.Supervisor,
            new MeetingChannelCreateRequest("ZOOM", "Weekly", "https://example.test/meeting"),
            TestContext.Current.CancellationToken);

        await auth.Received(1).EnsureCanManageAsync(projectId, Arg.Any<CancellationToken>());
        await auth.DidNotReceive().EnsureCanAccessAsync(projectId, Arg.Any<CancellationToken>());
        await repository.Received(1).AddAsync(Arg.Is<MeetingChannel>(x => x.ProjectId == projectId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChannelCreate_InvalidRole_PerformsNoAuthorizationProfileOrPersistenceCalls()
    {
        var repository = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        var sut = new MeetingChannelService(repository, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await Assert.ThrowsAsync<ApiException>(() => sut.CreateAsync(projectId, Guid.NewGuid(), "ADMIN",
            new MeetingChannelCreateRequest("ZOOM", "Weekly", "https://example.test/meeting"),
            TestContext.Current.CancellationToken));

        await auth.DidNotReceiveWithAnyArgs().EnsureCanManageAsync(default, default);
        await auth.DidNotReceiveWithAnyArgs().EnsureCanAccessAsync(default, default);
        await profile.DidNotReceiveWithAnyArgs().GetCurrentUserDisplayNameAsync(default);
        await repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task RecordCreate_InvalidChannel_DoesNotPersistOrResolveProfile()
    {
        var records = Substitute.For<IMeetingRecordRepository>();
        var channels = Substitute.For<IMeetingChannelRepository>();
        var auth = Substitute.For<IProjectAuthorizationClient>();
        var profile = Substitute.For<IUserProfileClient>();
        channels.FindAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MeetingChannel?)null);
        var sut = new MeetingRecordService(records, channels, auth, profile, new FixedTimeProvider(Now));
        var projectId = Guid.NewGuid();

        await Assert.ThrowsAsync<ApiValidationException>(() => sut.CreateAsync(projectId, Guid.NewGuid(), AuthSecurityConstants.Roles.Student,
            new MeetingRecordUpsertRequest("2026-10-09", 30, "Discussed progress", null, Guid.NewGuid()),
            TestContext.Current.CancellationToken));

        await auth.Received(1).EnsureCanAccessAsync(projectId, Arg.Any<CancellationToken>());
        await profile.DidNotReceiveWithAnyArgs().GetCurrentUserDisplayNameAsync(default);
        await records.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
