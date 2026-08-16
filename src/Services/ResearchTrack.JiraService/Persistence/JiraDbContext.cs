using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.JiraService.Persistence;

public sealed class JiraDbContext : DbContext
{
    public JiraDbContext(DbContextOptions<JiraDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
