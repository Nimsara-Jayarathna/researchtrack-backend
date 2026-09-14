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
    public DbSet<GitHubInstallationFlowState> InstallationFlowStates => Set<GitHubInstallationFlowState>();
    public DbSet<GitHubRepositoryAccessRequest> RepositoryAccessRequests => Set<GitHubRepositoryAccessRequest>();
    public DbSet<GitHubRepository> Repositories => Set<GitHubRepository>();
    public DbSet<ProjectRepositoryLink> ProjectRepositoryLinks => Set<ProjectRepositoryLink>();
    public DbSet<GitHubCommit> Commits => Set<GitHubCommit>();
    public DbSet<GitHubContributor> Contributors => Set<GitHubContributor>();
    public DbSet<GitHubPullRequest> PullRequests => Set<GitHubPullRequest>();
    public DbSet<GitHubPullRequestReview> PullRequestReviews => Set<GitHubPullRequestReview>();
    public DbSet<GitHubBranch> Branches => Set<GitHubBranch>();
    public DbSet<GitHubSyncRun> SyncRuns => Set<GitHubSyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GitHubDbContext).Assembly);
    }
}
