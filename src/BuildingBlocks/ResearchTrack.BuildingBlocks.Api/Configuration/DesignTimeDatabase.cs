namespace ResearchTrack.BuildingBlocks.Api.Configuration;

/// <summary>
/// Provides a non-secret, non-routable-at-deployment connection string for EF Core design-time operations.
/// The design-time factories use this only to configure the MySQL provider while creating migrations
/// and migration bundles. Real Test/Production credentials are supplied only at deployment/runtime.
/// </summary>
public static class DesignTimeDatabase
{
    public static string CreateConnectionString(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        return $"Server=127.0.0.1;Port=3306;Database={databaseName};User=design;Password=design;SslMode=None;AllowPublicKeyRetrieval=true;";
    }
}
