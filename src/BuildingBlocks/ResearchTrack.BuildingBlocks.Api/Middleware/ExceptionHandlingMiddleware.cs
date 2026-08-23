using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Infrastructure;

namespace ResearchTrack.BuildingBlocks.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiValidationException exception)
        {
            if (!context.Response.HasStarted)
            {
                await ApiErrorResponseWriter.WriteAsync(
                    context,
                    exception.StatusCode,
                    exception.Code,
                    exception.Message,
                    fieldErrors: exception.FieldErrors);
            }
        }
        catch (ApiException exception)
        {
            if (!context.Response.HasStarted)
            {
                await ApiErrorResponseWriter.WriteAsync(
                    context,
                    exception.StatusCode,
                    exception.Code,
                    exception.Message,
                    exception.Details);
            }
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(exception, "Unhandled exception after response started. TraceId={TraceId}", context.TraceIdentifier);
                throw;
            }

            if (DatabaseUnavailableDetector.IsDatabaseUnavailable(exception))
            {
                _logger.LogWarning(exception, "Database unavailable. TraceId={TraceId}", context.TraceIdentifier);
                await ApiErrorResponseWriter.WriteAsync(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.ServiceUnavailable,
                    "The service is temporarily unavailable.");
                return;
            }

            _logger.LogError(exception, "Unhandled API exception. TraceId={TraceId}", context.TraceIdentifier);
            await ApiErrorResponseWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                "An unexpected error occurred.");
        }
    }
}
