using ResearchTrack.ProjectService.Features.Projects;
using ResearchTrack.ProjectService.Infrastructure;

namespace ResearchTrack.ProjectService.Extensions;

public static class ProjectFeatureExtensions
{
    public static IServiceCollection AddProjectFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProjectService, ResearchTrack.ProjectService.Features.Projects.ProjectService>();
        services.AddHttpContextAccessor();

        var authBaseUrl = configuration["Services:Auth:BaseUrl"];
        if (string.IsNullOrWhiteSpace(authBaseUrl)
            || authBaseUrl.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Required configuration 'Services:Auth:BaseUrl' is missing or still contains a placeholder value.");
        }

        services.AddHttpClient<IAuthUserDirectoryClient, AuthUserDirectoryClient>(client =>
        {
            client.BaseAddress = new Uri(authBaseUrl.Trim(), UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
