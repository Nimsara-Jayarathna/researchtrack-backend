using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResearchTrack.GitHubService.Persistence;

public sealed class GitHubDbContextFactory : IDesignTimeDbContextFactory<GitHubDbContext>
{
    public GitHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection must be exported before running EF Core design-time commands. Use the repository migration scripts.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GitHubDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new GitHubDbContext(optionsBuilder.Options);
    }
}
