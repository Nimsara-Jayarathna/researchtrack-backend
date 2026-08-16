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
                ["Frontend:AllowedOrigins:0"] = "http://localhost:5173"
            };

            if (!string.IsNullOrWhiteSpace(_connectionString))
            {
                values["ConnectionStrings:DefaultConnection"] = _connectionString;
            }

            configuration.AddInMemoryCollection(values);
        });
    }
}
