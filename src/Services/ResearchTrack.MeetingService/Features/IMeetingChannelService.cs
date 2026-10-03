using ResearchTrack.MeetingService.Contracts;

namespace ResearchTrack.MeetingService.Features;

public interface IMeetingChannelService
{
    Task<IReadOnlyList<MeetingChannelResponse>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<MeetingChannelResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        string role,
        MeetingChannelUpsertRequest request,
        CancellationToken cancellationToken);

    Task<MeetingChannelResponse> UpdateAsync(
        Guid projectId,
        Guid channelId,
        MeetingChannelUpsertRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken);

    Task<MeetingChannelResponse> ApproveAsync(
        Guid projectId,
        Guid channelId,
        Guid supervisorId,
        CancellationToken cancellationToken);
}
