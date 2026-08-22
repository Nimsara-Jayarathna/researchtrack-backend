using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Configuration;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.ProjectService.Persistence;

public static class ProjectPersistenceExtensions
{
    public static IServiceCollection AddProjectPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        services.AddDbContextFactory<ProjectDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<ProjectDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
