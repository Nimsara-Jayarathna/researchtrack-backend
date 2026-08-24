namespace ResearchTrack.AuthService.Configuration;

public sealed record PasswordHashingOptions(int Iterations, int SaltSizeBytes, int HashSizeBytes)
{
    public static PasswordHashingOptions FromConfiguration(IConfiguration configuration) => new(
        RequireInt(configuration, "PasswordHashing:Iterations", 10000, 5000000),
        RequireInt(configuration, "PasswordHashing:SaltSizeBytes", 16, 128),
        RequireInt(configuration, "PasswordHashing:HashSizeBytes", 16, 128));

    private static int RequireInt(IConfiguration configuration, string key, int min, int max)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new InvalidOperationException($"Configuration '{key}' must be an integer between {min} and {max}.");
        }
        return value;
    }
}
