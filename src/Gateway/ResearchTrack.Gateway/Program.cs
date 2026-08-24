using System.Threading.RateLimiting;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Extensions;
using ResearchTrack.BuildingBlocks.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Keep config/env/gateway/.env.example as the single gateway configuration contract.
// Local scripts already map these friendly variables to ASP.NET configuration keys;
// container deployments inject the same variables directly, so map them here as well.
var gatewayEnvironmentOverrides = new Dictionary<string, string?>
{
    ["Frontend:AllowedOrigins:0"] = Environment.GetEnvironmentVariable("FRONTEND_ORIGIN"),
    ["ReverseProxy:Clusters:auth:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("AUTH_SERVICE_URL"),
    ["ReverseProxy:Clusters:project:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("PROJECT_SERVICE_URL"),
    ["ReverseProxy:Clusters:github:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("GITHUB_SERVICE_URL"),
    ["ReverseProxy:Clusters:jira:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("JIRA_SERVICE_URL"),
    ["ReverseProxy:Clusters:meeting:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("MEETING_SERVICE_URL"),
    ["ReverseProxy:Clusters:submission:Destinations:primary:Address"] = Environment.GetEnvironmentVariable("SUBMISSION_SERVICE_URL")
};

builder.Configuration.AddInMemoryCollection(
    gatewayEnvironmentOverrides.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)));

builder.Services.AddResearchTrackApi("ResearchTrack API Gateway");
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var allowedOrigins = builder.Configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("At least one Frontend:AllowedOrigins entry is required for the gateway.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value ?? string.Empty;

        var (bucket, permitLimit) = path switch
        {
            "/api/v1/auth/login" => ("auth-login", 10),
            "/api/v1/auth/refresh" => ("auth-refresh", 30),
            _ => ("general", 120)
        };

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{bucket}:{remoteIp}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.OnRejected = async (context, _) =>
    {
        if (!context.HttpContext.Response.HasStarted)
        {
            await ApiErrorResponseWriter.WriteAsync(
                context.HttpContext,
                StatusCodes.Status429TooManyRequests,
                ErrorCodes.RateLimited,
                "Too many requests. Please try again later.");
        }
    };
});

var app = builder.Build();
app.UseResearchTrackApi();
app.UseCors("frontend");
app.UseRateLimiter();
app.MapReverseProxy();
app.Run();

public partial class Program;
