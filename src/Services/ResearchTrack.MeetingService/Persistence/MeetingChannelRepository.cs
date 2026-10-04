using Microsoft.EntityFrameworkCore;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingChannelRepository : IMeetingChannelRepository
{
    private readonly IDbContextFactory<MeetingDbContext> _dbContextFactory;

    public MeetingChannelRepository(IDbContextFactory<MeetingDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<MeetingChannel>> ListByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.MeetingChannels
            .AsNoTracking()
            .Where(channel => channel.ProjectId == projectId)
            .OrderBy(channel =>
                channel.Status == MeetingChannelConstants.StatusPending ? 0 : 1)
            .ThenByDescending(channel => channel.CreatedAt)
            .ThenBy(channel => channel.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<MeetingChannel?> FindAsync(
        Guid projectId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.MeetingChannels.SingleOrDefaultAsync(
            channel => channel.Id == channelId && channel.ProjectId == projectId,
            cancellationToken);
    }

    public async Task AddAsync(
        MeetingChannel channel,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingChannels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(
        MeetingChannel channel,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingChannels.Update(channel);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        MeetingChannel channel,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MeetingChannels.Remove(channel);
        await db.SaveChangesAsync(cancellationToken);
    }
}
