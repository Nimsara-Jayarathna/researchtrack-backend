using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.ProjectService.Persistence;

public static class ProjectPersistenceExtensions
{
    public static IServiceCollection AddProjectPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack Project Service.");
        }

        services.AddDbContextFactory<ProjectDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<ProjectDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
