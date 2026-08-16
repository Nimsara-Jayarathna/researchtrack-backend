using Microsoft.EntityFrameworkCore;

namespace ResearchTrack.GitHubService.Persistence;

public sealed class GitHubDbContext : DbContext
{
    public GitHubDbContext(DbContextOptions<GitHubDbContext> options)
        : base(options)
    {
    }

    // Sprint feature implementations add service-owned entity sets here.
}
