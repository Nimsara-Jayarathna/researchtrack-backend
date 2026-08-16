using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.SubmissionService.Persistence;

public static class SubmissionPersistenceExtensions
{
    public static IServiceCollection AddSubmissionPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack Submission Service.");
        }

        services.AddDbContextFactory<SubmissionDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<SubmissionDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
