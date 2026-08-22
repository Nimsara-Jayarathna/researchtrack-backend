using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Configuration;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.MeetingService.Persistence;

public static class MeetingPersistenceExtensions
{
    public static IServiceCollection AddMeetingPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        services.AddDbContextFactory<MeetingDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<MeetingDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
