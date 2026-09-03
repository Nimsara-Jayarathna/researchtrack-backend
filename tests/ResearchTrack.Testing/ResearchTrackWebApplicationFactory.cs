using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ResearchTrack.Testing;

public sealed class ResearchTrackWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private static readonly object EnvironmentLock = new();

    private readonly string? _connectionString;
    private readonly Action<IServiceCollection>? _configureServices;

    public ResearchTrackWebApplicationFactory(
        string? connectionString = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _connectionString = connectionString;
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(GetTestConfigurationValues());
        });

        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var values = GetTestConfigurationValues();

        lock (EnvironmentLock)
        {
            var restoreValues = new Dictionary<string, string?>();
            SetEnvironmentValue("ASPNETCORE_ENVIRONMENT", "Testing", restoreValues);
            SetEnvironmentValue("DOTNET_ENVIRONMENT", "Testing", restoreValues);

            foreach (var (key, value) in values)
            {
                SetEnvironmentValue(key.Replace(":", "__"), value, restoreValues);
            }

            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                foreach (var (key, value) in restoreValues)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }

    private Dictionary<string, string?> GetTestConfigurationValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["OpenApi:Enabled"] = "true",

            ["Frontend:AllowedOrigins:0"] = "http://localhost:5173",

            ["ReverseProxy:Clusters:auth:Destinations:primary:Address"] =
                "http://localhost:5101/",

            ["ReverseProxy:Clusters:project:Destinations:primary:Address"] =
                "http://localhost:5102/",

            ["ReverseProxy:Clusters:github:Destinations:primary:Address"] =
                "http://localhost:5103/",

            ["ReverseProxy:Clusters:jira:Destinations:primary:Address"] =
                "http://localhost:5104/",

            ["ReverseProxy:Clusters:meeting:Destinations:primary:Address"] =
                "http://localhost:5105/",

            ["ReverseProxy:Clusters:submission:Destinations:primary:Address"] =
                "http://localhost:5106/",

            // Registration
            ["Registration:DomainRestrictionEnabled"] = "true",
            ["Registration:StudentEmailDomain"] = "students.example.edu",
            ["Registration:SupervisorEmailDomain"] = "staff.example.edu",
            ["Registration:StudentEmailPrefixRestrictionEnabled"] = "true",
            ["Registration:StudentIdentifierPattern"] = "^ST[0-9]{8}$",
            ["Registration:RequireStudentRegistrationNumber"] = "true",
            ["Registration:RequireStudentRegistrationNumberToMatchEmail"] = "true",
            ["Registration:MaxFirstNameLength"] = "100",
            ["Registration:MaxLastNameLength"] = "100",
            ["Registration:MaxEmailLength"] = "320",
            ["Registration:MaxRegistrationNumberLength"] = "20",
            ["Registration:OtpExpirySeconds"] = "600",
            ["Registration:SessionExpirySeconds"] = "600",

            // Password policy
            ["PasswordPolicy:MinimumLength"] = "12",
            ["PasswordPolicy:MaximumLength"] = "128",
            ["PasswordPolicy:RequireUppercase"] = "true",
            ["PasswordPolicy:RequireLowercase"] = "true",
            ["PasswordPolicy:RequireDigit"] = "true",
            ["PasswordPolicy:RequireSpecialCharacter"] = "true",

            // Password hashing
            ["PasswordHashing:Iterations"] = "10000",
            ["PasswordHashing:SaltSizeBytes"] = "16",
            ["PasswordHashing:HashSizeBytes"] = "32",

            // Brevo
            ["Brevo:BaseUrl"] = "https://api.example.test/v3/",
            ["Brevo:ApiKey"] = "test-api-key",
            ["Brevo:SenderEmail"] = "noreply@example.test",
            ["Brevo:SenderName"] = "ResearchTrack Tests",

            // JWT
            ["Jwt:Issuer"] = "ResearchTrack.AuthService.Tests",
            ["Jwt:Audience"] = "ResearchTrack.Tests",
            ["Jwt:SigningKey"] =
                "test-signing-key-that-is-at-least-32-bytes-long-123456789",
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "7",

            // Cookie
            ["Cookie:Secure"] = "false",

            // Services
            ["Services:Auth:BaseUrl"] = "http://localhost:5101/"
        };

        values["ConnectionStrings:DefaultConnection"] =
            string.IsNullOrWhiteSpace(_connectionString)
                ? TestDatabaseConfiguration.NonConnectingPlaceholder
                : _connectionString;

        return values;
    }

    private static void SetEnvironmentValue(
        string key,
        string? value,
        IDictionary<string, string?> restoreValues)
    {
        restoreValues.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }
}
