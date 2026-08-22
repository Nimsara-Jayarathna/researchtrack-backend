using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Configuration;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.SubmissionService.Persistence;

public static class SubmissionPersistenceExtensions
{
    public static IServiceCollection AddSubmissionPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        services.AddDbContextFactory<SubmissionDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<SubmissionDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
