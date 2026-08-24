using System.Data.Common;
using Microsoft.Extensions.Configuration;
using ResearchTrack.BuildingBlocks.Api.Configuration;

namespace ResearchTrack.AuthService.Tests.Configuration;

public sealed class DatabaseConnectionStringResolverTests
{
    [Theory]
    [InlineData("None", "Disabled")]
    [InlineData("none", "Disabled")]
    [InlineData("Disabled", "Disabled")]
    [InlineData("Required", "Required")]
    public void Resolve_normalizes_legacy_none_ssl_mode_for_database_configuration(
        string configuredSslMode,
        string expectedSslMode)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "127.0.0.1",
                ["Database:Port"] = "3307",
                ["Database:Name"] = "researchtrack_auth",
                ["Database:Username"] = "rt_auth",
                ["Database:Password"] = "secret",
                ["Database:SslMode"] = configuredSslMode,
                ["Database:AllowPublicKeyRetrieval"] = "true"
            })
            .Build();

        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);
        var values = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        Assert.Equal(expectedSslMode, values["SslMode"]);
    }

    [Fact]
    public void Resolve_returns_explicit_connection_string_without_normalizing_ssl_mode()
    {
        const string explicitConnectionString =
            "Server=mysql;Port=3306;Database=researchtrack_auth;User=rt_auth;Password=secret;SslMode=None;";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = explicitConnectionString
            })
            .Build();

        var connectionString = DatabaseConnectionStringResolver.Resolve(configuration);

        Assert.Equal(explicitConnectionString, connectionString);
    }
}
