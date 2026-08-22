namespace ResearchTrack.AuthService.Configuration;

public sealed record EmailOptions(
    string BaseUrl,
    string ApiKey,
    string SenderEmail,
    string SenderName)
{
    public static EmailOptions FromConfiguration(IConfiguration configuration) => new(
        NormalizeBaseUrl(configuration["Brevo:BaseUrl"]),
        Require(configuration, "Brevo:ApiKey"),
        Require(configuration, "Brevo:SenderEmail"),
        Require(configuration, "Brevo:SenderName"));

    private static string NormalizeBaseUrl(string? value)
    {
        var resolved = string.IsNullOrWhiteSpace(value)
            ? "https://api.brevo.com/v3/"
            : value.Trim();

        return resolved.EndsWith("/", StringComparison.Ordinal)
            ? resolved
            : resolved + "/";
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
        }

        return value.Trim();
    }
}
