using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;

namespace ResearchTrack.BuildingBlocks.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "HTTP {Method} {Path} started Service={Service} Environment={Environment} TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            _environment.ApplicationName,
            _environment.EnvironmentName,
            context.TraceIdentifier);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var routeTemplate = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            _logger.LogInformation(
                "HTTP {Method} {Path} completed Service={Service} Environment={Environment} StatusCode={StatusCode} DurationMs={DurationMs} TraceId={TraceId} Route={RouteTemplate} UserId={UserId}",
                context.Request.Method,
                context.Request.Path,
                _environment.ApplicationName,
                _environment.EnvironmentName,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                context.TraceIdentifier,
                routeTemplate,
                userId);
        }
    }
}
