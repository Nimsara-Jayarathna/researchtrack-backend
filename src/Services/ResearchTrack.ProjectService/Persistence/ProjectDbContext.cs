using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.ProjectService.Persistence;

public sealed class ProjectDbContext : DbContext
{
    public ProjectDbContext(DbContextOptions<ProjectDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
