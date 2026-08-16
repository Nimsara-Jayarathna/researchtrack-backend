using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.AuthService.Persistence;

public static class AuthPersistenceExtensions
{
    public static IServiceCollection AddAuthPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack Auth Service.");
        }

        services.AddDbContextFactory<AuthDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<AuthDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
