namespace ResearchTrack.Testing;

public static class TestDatabaseConfiguration
{
    public static string GetRequiredConnectionString(string service)
    {
        var key = $"RESEARCHTRACK_TEST_{service.ToUpperInvariant()}_CONNECTION";
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing {key}. Run database integration tests through ./scripts/test.sh integration after ./scripts/db-init.sh.");
        }

        if (!value.Contains("researchtrack_test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run destructive database integration tests because {key} does not target a researchtrack_test_* database.");
        }

        return value;
    }

    public static string NonConnectingPlaceholder =>
        "Server=127.0.0.1;Port=1;Database=researchtrack_test_placeholder;User=placeholder;Password=placeholder;Connection Timeout=1;";
}
