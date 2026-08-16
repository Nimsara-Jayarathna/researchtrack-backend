using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.SubmissionService.Persistence;

public sealed class SubmissionDbContext : DbContext
{
    public SubmissionDbContext(DbContextOptions<SubmissionDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
