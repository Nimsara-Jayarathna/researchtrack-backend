using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence;

public interface IMeetingRecordRepository
{
    Task<IReadOnlyList<MeetingRecord>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<MeetingRecord?> FindAsync(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken);

    Task AddAsync(MeetingRecord record, CancellationToken cancellationToken);
    Task SaveAsync(MeetingRecord record, CancellationToken cancellationToken);
    Task DeleteAsync(MeetingRecord record, CancellationToken cancellationToken);
}
