using Microsoft.EntityFrameworkCore;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Persistence;

public sealed class GitHubDbContext : DbContext
{
    public GitHubDbContext(DbContextOptions<GitHubDbContext> options)
        : base(options)
    {
    }

    public DbSet<GitHubAccessSource> AccessSources => Set<GitHubAccessSource>();
    public DbSet<GitHubRepository> Repositories => Set<GitHubRepository>();
    public DbSet<ProjectRepositoryLink> ProjectRepositoryLinks => Set<ProjectRepositoryLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GitHubDbContext).Assembly);
    }
}
