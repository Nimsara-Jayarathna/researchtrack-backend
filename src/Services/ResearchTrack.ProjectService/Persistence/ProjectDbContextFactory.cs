using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.ProjectService.Persistence;

public sealed class ProjectDbContextFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    public ProjectDbContext CreateDbContext(string[] args)
    {
        var connectionString = DatabaseConnectionStringResolver.ResolveFromEnvironment();

        var optionsBuilder = new DbContextOptionsBuilder<ProjectDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new ProjectDbContext(optionsBuilder.Options);
    }
}
