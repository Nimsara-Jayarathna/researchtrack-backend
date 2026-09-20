using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Infrastructure;

namespace ResearchTrack.JiraService.Extensions;

public static class JiraFeatureExtensions
{
    public static IServiceCollection AddJiraFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JiraOptions.SectionName).Get<JiraOptions>() ?? new JiraOptions();
        ValidateRequiredConfiguration(options, configuration);

        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddScoped<IJiraTokenProtector, JiraTokenProtector>();
        services.AddScoped<IJiraConnectionService, JiraConnectionService>();
        services.AddScoped<IJiraSyncService, JiraSyncService>();
        services.AddScoped<IJiraIssueQueryService, JiraIssueQueryService>();

        var projectUrl = configuration["Services:Project:BaseUrl"]!;
        services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>(client =>
        {
            client.BaseAddress = new Uri(projectUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.ProjectServiceTimeoutSeconds);
        });

        services.AddHttpClient<AtlassianClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(options.AtlassianTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return services;
    }

    private static void ValidateRequiredConfiguration(JiraOptions options, IConfiguration configuration)
    {
        static void Required(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Required environment configuration '{key}' is missing.");
        }

        Required(options.ClientId, "Jira__ClientId");
        Required(options.ClientSecret, "Jira__ClientSecret");
        Required(options.RedirectUri, "Jira__RedirectUri");
        Required(options.Scope, "Jira__Scope");
        Required(options.Audience, "Jira__Audience");
        Required(options.AuthorizationUrl, "Jira__AuthorizationUrl");
        Required(options.TokenUrl, "Jira__TokenUrl");
        Required(options.AccessibleResourcesUrl, "Jira__AccessibleResourcesUrl");
        Required(options.ApiBaseUrl, "Jira__ApiBaseUrl");
        Required(configuration["Services:Project:BaseUrl"] ?? string.Empty, "Services__Project__BaseUrl");

        if (options.OAuthStateTtlMinutes <= 0) throw new InvalidOperationException("Jira__OAuthStateTtlMinutes must be greater than zero.");
        if (options.SelectionTtlMinutes <= 0) throw new InvalidOperationException("Jira__SelectionTtlMinutes must be greater than zero.");
        if (options.AtlassianTimeoutSeconds <= 0) throw new InvalidOperationException("Jira__AtlassianTimeoutSeconds must be greater than zero.");
        if (options.ProjectServiceTimeoutSeconds <= 0) throw new InvalidOperationException("Jira__ProjectServiceTimeoutSeconds must be greater than zero.");
    }
}
