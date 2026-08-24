namespace ResearchTrack.AuthService.Configuration;

public sealed record JwtOptions(
    string Issuer,
    string Audience,
    string SigningKey,
    int AccessTokenMinutes,
    int RefreshTokenDays)
{
    public static JwtOptions FromConfiguration(IConfiguration configuration) => new(
        Require(configuration, "Jwt:Issuer"),
        Require(configuration, "Jwt:Audience"),
        Require(configuration, "Jwt:SigningKey"),
        RequireInt(configuration, "Jwt:AccessTokenMinutes", 1, 1440),
        RequireInt(configuration, "Jwt:RefreshTokenDays", 1, 365));

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
        }

        return value.Trim();
    }

    private static int RequireInt(IConfiguration configuration, string key, int min, int max)
    {
        var raw = Require(configuration, key);
        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new InvalidOperationException($"Configuration '{key}' must be an integer between {min} and {max}.");
        }

        return value;
    }
}
