namespace ResearchTrack.AuthService.Configuration;

public sealed record AuthCookieOptions(bool Secure)
{
    public static AuthCookieOptions FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration["Cookie:Secure"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AuthCookieOptions(false);
        }

        return bool.TryParse(raw, out var secure)
            ? new AuthCookieOptions(secure)
            : throw new InvalidOperationException("Configuration 'Cookie:Secure' must be true or false.");
    }
}
