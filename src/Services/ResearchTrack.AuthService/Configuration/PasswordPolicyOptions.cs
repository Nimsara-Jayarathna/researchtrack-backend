namespace ResearchTrack.AuthService.Configuration;

public sealed record PasswordPolicyOptions(
    int MinimumLength,
    int MaximumLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialCharacter)
{
    public static PasswordPolicyOptions FromConfiguration(IConfiguration configuration) => new(
        RequireInt(configuration, "PasswordPolicy:MinimumLength", 1, 1024),
        RequireInt(configuration, "PasswordPolicy:MaximumLength", 1, 4096),
        RequireBool(configuration, "PasswordPolicy:RequireUppercase"),
        RequireBool(configuration, "PasswordPolicy:RequireLowercase"),
        RequireBool(configuration, "PasswordPolicy:RequireDigit"),
        RequireBool(configuration, "PasswordPolicy:RequireSpecialCharacter"));

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
        }
        return value.Trim();
    }

    private static bool RequireBool(IConfiguration configuration, string key)
    {
        var raw = Require(configuration, key);
        return bool.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
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
