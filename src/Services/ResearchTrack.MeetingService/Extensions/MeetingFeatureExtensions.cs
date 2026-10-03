using ResearchTrack.MeetingService.Configuration;
using ResearchTrack.MeetingService.Features;
using ResearchTrack.MeetingService.Infrastructure;
using ResearchTrack.MeetingService.Persistence;

namespace ResearchTrack.MeetingService.Extensions;

public static class MeetingFeatureExtensions
{
    public static IServiceCollection AddMeetingFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(MeetingOptions.SectionName)
            .Get<MeetingOptions>() ?? new MeetingOptions();

        if (options.DependencyTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Meeting__DependencyTimeoutSeconds must be greater than zero.");
        }

        var projectBaseUrl = RequireUrl(configuration, "Services:Project:BaseUrl");
        var authBaseUrl = RequireUrl(configuration, "Services:Auth:BaseUrl");

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<IMeetingChannelRepository, MeetingChannelRepository>();
        services.AddScoped<IMeetingRecordRepository, MeetingRecordRepository>();
        services.AddScoped<IMeetingChannelService, MeetingChannelService>();
        services.AddScoped<IMeetingRecordService, MeetingRecordService>();

        services.AddHttpClient<IProjectAuthorizationClient, ProjectAuthorizationClient>(
            client =>
            {
                client.BaseAddress = EnsureTrailingSlash(projectBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.DependencyTimeoutSeconds);
            });

        services.AddHttpClient<IUserProfileClient, UserProfileClient>(
            client =>
            {
                client.BaseAddress = EnsureTrailingSlash(authBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.DependencyTimeoutSeconds);
            });

        return services;
    }

    private static string RequireUrl(IConfiguration configuration, string key)
    {
        var raw = configuration[key];

        if (string.IsNullOrWhiteSpace(raw)
            || raw.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Required environment configuration '{key.Replace(':', '_')}' is missing or invalid.");
        }

        return raw.Trim();
    }

    private static Uri EnsureTrailingSlash(string raw) =>
        new(raw.TrimEnd('/') + "/", UriKind.Absolute);
}
