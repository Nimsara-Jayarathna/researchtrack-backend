using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.JiraService.Persistence;

public sealed class JiraDbContextFactory : IDesignTimeDbContextFactory<JiraDbContext>
{
    public JiraDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<JiraDbContext>();
        optionsBuilder.UseMySQL(
            DesignTimeDatabase.CreateConnectionString("researchtrack_jira_design"));

        return new JiraDbContext(optionsBuilder.Options);
    }
}
