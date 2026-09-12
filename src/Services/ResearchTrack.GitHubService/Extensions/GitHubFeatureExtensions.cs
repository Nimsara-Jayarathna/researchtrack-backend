using System.Net.Http.Headers;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Extensions;

public static class GitHubFeatureExtensions
{
    public static IServiceCollection AddGitHubFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var projectBaseUrl = RequireAbsoluteUri(configuration, "Services:Project:BaseUrl");
        var linkOptions = GetRepositoryLinkOptions(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(linkOptions);
        services.AddSingleton(_ => GitHubAppOptionsFactory.Create(configuration));
        services.AddHttpContextAccessor();
        services.AddScoped<IPublicAccessSourceService, PublicAccessSourceService>();
        services.AddScoped<IGitHubInstallationStateService, GitHubInstallationStateService>();
        services.AddScoped<IGitHubInstallationStateStore, GitHubInstallationStateStore>();
        services.AddScoped<IGitHubInstallationFlowService, GitHubInstallationFlowService>();
        services.AddScoped<IGitHubInstallationRepositoryService, GitHubInstallationRepositoryService>();
        services.AddScoped<IInstallationAccessSourceStore, InstallationAccessSourceStore>();
        services.AddScoped<IInstallationRepositoryStore, InstallationRepositoryStore>();
        services.AddSingleton<IGitHubAppJwtProvider, GitHubAppJwtProvider>();
        services.AddSingleton<GitHubOAuthPkce>();
        services.AddScoped<IPublicAccessSourceStore, PublicAccessSourceStore>();
        services.AddScoped<IRepositoryLinkService, RepositoryLinkService>();
        services.AddScoped<IRepositoryLinkStore, RepositoryLinkStore>();
        services.AddSingleton<IInitialRepositorySyncRequester, DeferredInitialRepositorySyncRequester>();

        services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(projectBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });


        services.AddHttpClient<IGitHubAppClient, GitHubAppClient>(ConfigureGitHubApiClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddHttpClient<IGitHubInstallationApiClient, GitHubInstallationApiClient>(ConfigureGitHubApiClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddHttpClient<IGitHubUserInstallationClient, GitHubUserInstallationClient>(ConfigureGitHubApiClient)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddHttpClient<IGitHubUserAuthorizationClient, GitHubUserAuthorizationClient>(client =>
        {
            client.BaseAddress = GitHubUserAuthorizationClient.TrustedBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ResearchTrack", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        services.AddScoped<IGitHubInstallationRepositoryClient, GitHubInstallationRepositoryClient>();

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


    private static void ConfigureGitHubApiClient(HttpClient client)
    {
        client.BaseAddress = GitHubPublicRepositoryClient.TrustedBaseAddress;
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ResearchTrack", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
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

    private static GitHubRepositoryLinkOptions GetRepositoryLinkOptions(IConfiguration configuration)
    {
        var linked = configuration.GetValue<int>("GitHub:RepositoryLinks:MaxLinkedRepositories");
        var enabled = configuration.GetValue<int>("GitHub:RepositoryLinks:MaxEnabledRepositories");
        if (linked < 1 || enabled < 1 || enabled > linked)
        {
            throw new InvalidOperationException(
                "GitHub repository limits must be positive and enabled repositories cannot exceed linked repositories.");
        }

        return new GitHubRepositoryLinkOptions(linked, enabled);
    }
}
