using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.GitHubService.Persistence;

public sealed class GitHubDbContextFactory : IDesignTimeDbContextFactory<GitHubDbContext>
{
    public GitHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = DatabaseConnectionStringResolver.ResolveFromEnvironment();

        var optionsBuilder = new DbContextOptionsBuilder<GitHubDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new GitHubDbContext(optionsBuilder.Options);
    }
}
