using ResearchTrack.MeetingService.Contracts;

namespace ResearchTrack.MeetingService.Features;

public interface IMeetingRecordService
{
    Task<IReadOnlyList<MeetingRecordResponse>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<MeetingRecordResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        string role,
        MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken);

    Task<MeetingRecordResponse> UpdateAsync(
        Guid projectId,
        Guid recordId,
        MeetingRecordUpsertRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken);

    Task<MeetingRecordResponse> ApproveAsync(
        Guid projectId,
        Guid recordId,
        Guid supervisorId,
        CancellationToken cancellationToken);
}
