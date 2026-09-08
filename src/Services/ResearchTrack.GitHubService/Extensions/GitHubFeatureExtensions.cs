using System.Net.Http.Headers;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Extensions;

public static class GitHubFeatureExtensions
{
    public static IServiceCollection AddGitHubFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var projectBaseUrl = RequireAbsoluteUri(configuration, "Services:Project:BaseUrl");

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<IPublicAccessSourceService, PublicAccessSourceService>();
        services.AddScoped<IPublicAccessSourceStore, PublicAccessSourceStore>();

        services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(projectBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<IGitHubPublicRepositoryClient, GitHubPublicRepositoryClient>(client =>
        {
            client.BaseAddress = GitHubPublicRepositoryClient.TrustedBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ResearchTrack", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        return services;
    }

    private static Uri RequireAbsoluteUri(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Required configuration '{key}' must be an absolute HTTP or HTTPS URI.");
        }

        return uri;
    }

    private static Uri EnsureTrailingSlash(Uri value) => new(
        value.ToString().TrimEnd('/') + "/",
        UriKind.Absolute);
}
