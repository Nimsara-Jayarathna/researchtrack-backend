using System.Diagnostics;

namespace ResearchTrack.BuildingBlocks.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(supplied)
            ? supplied!
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
