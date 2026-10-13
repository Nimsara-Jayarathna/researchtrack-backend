using Swashbuckle.AspNetCore.SwaggerUI;

namespace ResearchTrack.Gateway;

/// <summary>Publishes only the known services' v1 documents through existing YARP clusters.</summary>
public static class ServiceOpenApi
{
    public static readonly IReadOnlyList<string> Services = Array.AsReadOnly(
        new[] { "auth", "project", "github", "jira", "meeting", "submission" });

    public static void ConfigureRoutes(ConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !configuration.GetValue<bool>("OpenApi:Enabled"))
        {
            return;
        }

        var routes = new Dictionary<string, string?>();
        foreach (var service in Services)
        {
            var prefix = $"ReverseProxy:Routes:swagger-{service}";
            routes[$"{prefix}:ClusterId"] = service;
            routes[$"{prefix}:Match:Path"] = $"/swagger/{service}/v1/swagger.json";
            routes[$"{prefix}:Match:Methods:0"] = "GET";
            routes[$"{prefix}:Transforms:0:PathSet"] = "/swagger/v1/swagger.json";
        }

        configuration.AddInMemoryCollection(routes);
    }

    public static void ConfigureUi(SwaggerUIOptions options)
    {
        // Relative URLs keep the UI usable behind a reverse proxy with a path base.
        foreach (var service in Services)
        {
            options.SwaggerEndpoint($"{service}/v1/swagger.json", $"ResearchTrack {service} v1");
        }

        options.SwaggerEndpoint("v1/swagger.json", "ResearchTrack Gateway v1 (gateway endpoints only)");
    }
}
