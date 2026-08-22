using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ResearchTrack.Testing;

public sealed class ResearchTrackWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly string? _connectionString;

    public ResearchTrackWebApplicationFactory(string? connectionString = null)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["OpenApi:Enabled"] = "true",
                ["Frontend:AllowedOrigins:0"] = "http://localhost:5173",
                ["ReverseProxy:Clusters:auth:Destinations:primary:Address"] = "http://localhost:5101/",
                ["ReverseProxy:Clusters:project:Destinations:primary:Address"] = "http://localhost:5102/",
                ["ReverseProxy:Clusters:github:Destinations:primary:Address"] = "http://localhost:5103/",
                ["ReverseProxy:Clusters:jira:Destinations:primary:Address"] = "http://localhost:5104/",
                ["ReverseProxy:Clusters:meeting:Destinations:primary:Address"] = "http://localhost:5105/",
                ["ReverseProxy:Clusters:submission:Destinations:primary:Address"] = "http://localhost:5106/",
                ["Registration:StudentEmailDomain"] = "students.example.edu",
                ["Registration:SupervisorEmailDomain"] = "staff.example.edu",
                ["Registration:StudentIdentifierPattern"] = "^ST[0-9]{8}$",
                ["Registration:RequireStudentRegistrationNumber"] = "true",
                ["Registration:RequireStudentRegistrationNumberToMatchEmail"] = "true",
                ["Registration:MaxFirstNameLength"] = "100",
                ["Registration:MaxLastNameLength"] = "100",
                ["Registration:MaxEmailLength"] = "320",
                ["Registration:MaxRegistrationNumberLength"] = "20",
                ["PasswordPolicy:MinimumLength"] = "12",
                ["PasswordPolicy:MaximumLength"] = "128",
                ["PasswordPolicy:RequireUppercase"] = "true",
                ["PasswordPolicy:RequireLowercase"] = "true",
                ["PasswordPolicy:RequireDigit"] = "true",
                ["PasswordPolicy:RequireSpecialCharacter"] = "true",
                ["PasswordHashing:Iterations"] = "10000",
                ["PasswordHashing:SaltSizeBytes"] = "16",
                ["PasswordHashing:HashSizeBytes"] = "32"
            };

            if (!string.IsNullOrWhiteSpace(_connectionString))
            {
                values["ConnectionStrings:DefaultConnection"] = _connectionString;
            }

            configuration.AddInMemoryCollection(values);
        });
    }
}
