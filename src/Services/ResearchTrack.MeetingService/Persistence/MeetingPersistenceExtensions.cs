using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.MeetingService.Persistence;

public static class MeetingPersistenceExtensions
{
    public static IServiceCollection AddMeetingPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack Meeting Service.");
        }

        services.AddDbContextFactory<MeetingDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<MeetingDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
