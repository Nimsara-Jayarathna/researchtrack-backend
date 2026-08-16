using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.MeetingService.Persistence;

public sealed class MeetingDbContext : DbContext
{
    public MeetingDbContext(DbContextOptions<MeetingDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
