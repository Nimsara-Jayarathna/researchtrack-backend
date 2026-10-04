using Microsoft.EntityFrameworkCore;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingRecordRepository : IMeetingRecordRepository
{
    private readonly IDbContextFactory<MeetingDbContext> _dbContextFactory;

    public MeetingRecordRepository(IDbContextFactory<MeetingDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<MeetingRecord>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.MeetingRecords
            .AsNoTracking()
            .Where(record => record.ProjectId == projectId)
            .OrderBy(record =>
                record.Status == MeetingRecordConstants.StatusPending ? 0 : 1)
            .ThenByDescending(record => record.MeetingDate)
            .ThenByDescending(record => record.CreatedAt)
            .ThenBy(record => record.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<MeetingRecord?> FindAsync(
        Guid projectId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.MeetingRecords.SingleOrDefaultAsync(
            record => record.Id == recordId && record.ProjectId == projectId,
            cancellationToken);
    }

    public async Task AddAsync(MeetingRecord record, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(MeetingRecord record, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingRecords.Update(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MeetingRecord record, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken);
    }
}
