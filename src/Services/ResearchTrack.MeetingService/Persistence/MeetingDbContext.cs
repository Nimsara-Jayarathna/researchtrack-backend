using Microsoft.EntityFrameworkCore;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingDbContext : DbContext
{
    public MeetingDbContext(DbContextOptions<MeetingDbContext> options)
        : base(options)
    {
    }

    public DbSet<MeetingChannel> MeetingChannels => Set<MeetingChannel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeetingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
