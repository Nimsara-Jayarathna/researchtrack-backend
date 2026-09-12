namespace ResearchTrack.AuthService.Configuration;

public sealed record PasswordResetOptions(
    int TokenExpiryMinutes,
    Uri FrontendBaseUri)
{
    public static PasswordResetOptions FromConfiguration(IConfiguration configuration)
    {
        var expiry = RequireInt(
            configuration,
            "PasswordReset:TokenExpiryMinutes",
            5,
            1440);
        var rawBaseUrl = Require(configuration, "PasswordReset:FrontendBaseUrl");

        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException(
                "PasswordReset:FrontendBaseUrl must be an absolute HTTP(S) URL without a query or fragment.");
        }

        return new PasswordResetOptions(expiry, baseUri);
    }

    public string CreateResetUrl(string rawToken) =>
        $"{FrontendBaseUri.ToString().TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Required configuration '{key}' is missing or still contains a placeholder value.");
        }

        return value.Trim();
    }

    private static int RequireInt(
        IConfiguration configuration,
        string key,
        int minimum,
        int maximum)
    {
        var raw = Require(configuration, key);
        if (!int.TryParse(raw, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be an integer between {minimum} and {maximum}.");
        }

        return value;
    }
}
