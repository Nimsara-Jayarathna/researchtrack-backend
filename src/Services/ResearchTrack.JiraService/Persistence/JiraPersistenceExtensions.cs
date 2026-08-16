using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.JiraService.Persistence;

public static class JiraPersistenceExtensions
{
    public static IServiceCollection AddJiraPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack Jira Service.");
        }

        services.AddDbContextFactory<JiraDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<JiraDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
