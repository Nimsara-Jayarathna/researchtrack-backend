namespace ResearchTrack.AuthService.Configuration;

public sealed record AuthCookieOptions(bool Secure)
{
    public static AuthCookieOptions FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var raw = configuration["Cookie:Secure"];
        var allowInsecure = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var secure =
            string.IsNullOrWhiteSpace(raw) ? !allowInsecure
            : bool.TryParse(raw, out var configuredSecure) ? configuredSecure
            : throw new InvalidOperationException("Configuration 'Cookie:Secure' must be true or false.");

        if (!secure && !allowInsecure)
        {
            throw new InvalidOperationException(
                "Cookie:Secure must be true outside Development/Testing to protect authentication cookies."
            );
        }

        return new AuthCookieOptions(secure);
    }
}
