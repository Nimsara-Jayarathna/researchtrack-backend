using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace ResearchTrack.BuildingBlocks.Api.Configuration;

public static class DatabaseConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var explicitConnectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var host = Require(configuration, "Database:Host");
        var port = RequireInt(configuration, "Database:Port", 1, 65535);
        var name = Require(configuration, "Database:Name");
        var username = Require(configuration, "Database:Username");
        var password = Require(configuration, "Database:Password");
        var sslMode = NormalizeSslMode(Require(configuration, "Database:SslMode"));
        var allowPublicKeyRetrieval = RequireBool(configuration, "Database:AllowPublicKeyRetrieval");

        var builder = new DbConnectionStringBuilder
        {
            ["Server"] = host,
            ["Port"] = port,
            ["Database"] = name,
            ["User"] = username,
            ["Password"] = password,
            ["SslMode"] = sslMode,
            ["AllowPublicKeyRetrieval"] = allowPublicKeyRetrieval
        };

        return builder.ConnectionString;
    }

    public static string ResolveFromEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        return Resolve(configuration);
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || IsPlaceholder(value))
        {
            throw new InvalidOperationException($"Required configuration '{key}' is missing or still contains a placeholder value.");
        }

        return value;
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

    private static bool RequireBool(IConfiguration configuration, string key)
    {
        var raw = Require(configuration, key);
        if (!bool.TryParse(raw, out var value))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
        }

        return value;
    }

    private static string NormalizeSslMode(string value) =>
        value.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? "Disabled"
            : value;

    private static bool IsPlaceholder(string value) =>
        value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || value.Equals("__SET_ME__", StringComparison.OrdinalIgnoreCase)
        || value.Equals("__GENERATE__", StringComparison.OrdinalIgnoreCase);
}
