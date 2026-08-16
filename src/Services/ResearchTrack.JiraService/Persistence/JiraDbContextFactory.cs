using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResearchTrack.JiraService.Persistence;

public sealed class JiraDbContextFactory : IDesignTimeDbContextFactory<JiraDbContext>
{
    public JiraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection must be exported before running EF Core design-time commands. Use the repository migration scripts.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<JiraDbContext>();
        optionsBuilder.UseMySQL(connectionString);
        return new JiraDbContext(optionsBuilder.Options);
    }
}
