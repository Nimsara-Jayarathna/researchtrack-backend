namespace ResearchTrack.BuildingBlocks.Api.Configuration;

/// <summary>
/// Provides the connection string used by EF Core design-time operations.
/// Repository scripts load the target service's environment before invoking EF,
/// so migrations are applied to the same database as the running service.
/// When no service environment is loaded, a non-secret local design placeholder
/// is used only so EF can create migrations and migration bundles.
/// </summary>
public static class DesignTimeDatabase
{
    public static string CreateConnectionString(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        try
        {
            return DatabaseConnectionStringResolver.ResolveFromEnvironment();
        }
        catch (InvalidOperationException)
        {
            // Migration generation/bundling can run without service-specific runtime
            // configuration. In that case EF only needs provider metadata.
        }

        return $"Server=127.0.0.1;Port=3306;Database={databaseName};User=design;Password=design;SslMode=Disabled;AllowPublicKeyRetrieval=true;";
    }
}
