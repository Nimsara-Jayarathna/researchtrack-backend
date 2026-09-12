namespace ResearchTrack.GitHubService.Configuration;

public static class GitHubAppOptionsFactory
{
    public static GitHubAppOptions Create(IConfiguration configuration)
    {
        var appId = configuration.GetValue<long>("GitHub:AppId");
        if (appId <= 0)
        {
            throw Invalid("GitHub:AppId", "must be a positive GitHub App id.");
        }

        var appSlug = RequireValue(configuration, "GitHub:AppSlug");
        if (appSlug.Contains('/') || appSlug.Contains(' '))
        {
            throw Invalid("GitHub:AppSlug", "must be the GitHub App slug, not a URL.");
        }

        var clientId = RequireValue(configuration, "GitHub:ClientId");
        var clientSecret = RequireValue(configuration, "GitHub:ClientSecret");
        var privateKeyPath = RequireValue(configuration, "GitHub:PrivateKeyPath");
        var setupCallbackUrl = RequireHttpsUrl(configuration, "GitHub:SetupCallbackUrl", allowLocalHttp: true);
        var frontendReturnOrigin = RequireHttpsOrigin(configuration, "GitHub:FrontendReturnOrigin", allowLocalHttp: true);
        var stateExpiryMinutes = configuration.GetValue<int>("GitHub:StateExpiryMinutes");
        if (stateExpiryMinutes is < 1 or > 30)
        {
            throw Invalid("GitHub:StateExpiryMinutes", "must be between 1 and 30 minutes.");
        }

        return new GitHubAppOptions(
            appId,
            appSlug,
            clientId,
            clientSecret,
            privateKeyPath,
            setupCallbackUrl,
            frontendReturnOrigin,
            TimeSpan.FromMinutes(stateExpiryMinutes));
    }

    private static string RequireValue(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(key, "is required.");
        }

        return value;
    }

    private static Uri RequireHttpsUrl(
        IConfiguration configuration,
        string key,
        bool allowLocalHttp)
    {
        var value = RequireValue(configuration, key);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !IsAllowedScheme(uri, allowLocalHttp))
        {
            throw Invalid(key, "must be an absolute HTTPS URL (HTTP is allowed only for localhost development). ");
        }

        return uri;
    }

    private static Uri RequireHttpsOrigin(
        IConfiguration configuration,
        string key,
        bool allowLocalHttp)
    {
        var uri = RequireHttpsUrl(configuration, key, allowLocalHttp);
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid(key, "must be an origin only, without path, query, or fragment.");
        }

        return uri;
    }

    private static bool IsAllowedScheme(Uri uri, bool allowLocalHttp)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return allowLocalHttp
            && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static InvalidOperationException Invalid(string key, string detail) => new(
        $"Required GitHub App configuration '{key}' {detail}");
}
