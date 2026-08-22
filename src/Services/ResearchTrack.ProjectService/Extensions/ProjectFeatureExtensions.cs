using ResearchTrack.ProjectService.Features.Projects;

namespace ResearchTrack.ProjectService.Extensions;

public static class ProjectFeatureExtensions
{
    public static IServiceCollection AddProjectFeatures(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProjectService, ResearchTrack.ProjectService.Features.Projects.ProjectService>();
        return services;
    }
}
