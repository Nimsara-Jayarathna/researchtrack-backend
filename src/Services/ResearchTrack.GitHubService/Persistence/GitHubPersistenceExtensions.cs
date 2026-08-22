using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Configuration;
using ResearchTrack.BuildingBlocks.Api.Health;

namespace ResearchTrack.GitHubService.Persistence;

public static class GitHubPersistenceExtensions
{
    public static IServiceCollection AddGitHubPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        services.AddDbContextFactory<GitHubDbContext>(options => options.UseMySQL(connectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<GitHubDbContext>>(
            "mysql",
            tags: new[] { "ready" });

        return services;
    }
}
