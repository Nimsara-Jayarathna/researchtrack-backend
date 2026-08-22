using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Configuration;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.JiraService.Persistence;

public static class JiraPersistenceExtensions
{
    public static IServiceCollection AddJiraPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        services.AddDbContextFactory<JiraDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<JiraDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
