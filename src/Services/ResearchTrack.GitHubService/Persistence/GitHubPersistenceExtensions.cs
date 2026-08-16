using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.GitHubService.Persistence;

public static class GitHubPersistenceExtensions
{
    public static IServiceCollection AddGitHubPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for ResearchTrack GitHub Service.");
        }

        services.AddDbContextFactory<GitHubDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<GitHubDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
