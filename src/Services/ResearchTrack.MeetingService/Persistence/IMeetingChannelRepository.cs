using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence;

public interface IMeetingChannelRepository
{
    Task<IReadOnlyList<MeetingChannel>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<MeetingChannel?> FindAsync(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken);

    Task AddAsync(MeetingChannel channel, CancellationToken cancellationToken);
    Task SaveAsync(MeetingChannel channel, CancellationToken cancellationToken);
    Task DeleteAsync(MeetingChannel channel, CancellationToken cancellationToken);
}
