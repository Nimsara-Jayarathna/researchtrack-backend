using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;

namespace ResearchTrack.BuildingBlocks.Api.Infrastructure;

public static class ApiErrorResponseWriter
{
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        object? details = null,
        IReadOnlyList<ApiFieldError>? fieldErrors = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = ApiResponse<object>.Fail(
            new ApiError(code, message, fieldErrors, details),
            new ApiMeta(context.TraceIdentifier, DateTimeOffset.UtcNow));

        return context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }

    public static Task WriteStatusCodeAsync(HttpContext context)
    {
        var (code, message) = context.Response.StatusCode switch
        {
            StatusCodes.Status400BadRequest => (ErrorCodes.BadRequest, "The request could not be processed."),
            StatusCodes.Status401Unauthorized => (ErrorCodes.Unauthorized, "Authentication is required."),
            StatusCodes.Status403Forbidden => (ErrorCodes.Forbidden, "You do not have permission to perform this action."),
            StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "The requested resource was not found."),
            StatusCodes.Status409Conflict => (ErrorCodes.Conflict, "The request conflicts with the current resource state."),
            StatusCodes.Status429TooManyRequests => (ErrorCodes.RateLimited, "Too many requests. Please try again later."),
            StatusCodes.Status502BadGateway => (ErrorCodes.DependencyUnavailable, "A downstream service is unavailable."),
            StatusCodes.Status503ServiceUnavailable => (ErrorCodes.ServiceUnavailable, "The service is temporarily unavailable."),
            _ => (ErrorCodes.InternalError, "The request could not be completed.")
        };

        return WriteAsync(context, context.Response.StatusCode, code, message);
    }
}
