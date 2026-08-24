using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.GitHubService.Persistence;

public sealed class GitHubDbContextFactory : IDesignTimeDbContextFactory<GitHubDbContext>
{
    public GitHubDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GitHubDbContext>();
        optionsBuilder.UseMySQL(
            DesignTimeDatabase.CreateConnectionString("researchtrack_github_design"));

        return new GitHubDbContext(optionsBuilder.Options);
    }
}
