using System.Net;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.MeetingService.Infrastructure;

public sealed class ProjectAuthorizationClient : IProjectAuthorizationClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectAuthorizationClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task EnsureCanAccessAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        EnsureAsync(
            $"api/v1/projects/{projectId}/authorization/access",
            "You do not have access to this project.",
            cancellationToken);

    public Task EnsureCanManageAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        EnsureAsync(
            $"api/v1/projects/{projectId}/authorization/manage",
            "Only the owning Supervisor can manage this project's meeting channels.",
            cancellationToken);

    private async Task EnsureAsync(
        string relativePath,
        string forbiddenMessage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        ForwardAuthentication(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.DependencyUnavailable,
                "Project Service is unavailable.",
                innerException: exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ApiException(
                    StatusCodes.Status401Unauthorized,
                    ErrorCodes.Unauthorized,
                    "Authentication is required.");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                throw new ApiException(
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.Forbidden,
                    forbiddenMessage);
            }

            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.DependencyUnavailable,
                "Unable to verify project authorization with Project Service.");
        }
    }

    private void ForwardAuthentication(HttpRequestMessage request)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request
            ?? throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.DependencyUnavailable,
                "Request authentication context is unavailable.");

        if (incoming.Headers.Authorization.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                incoming.Headers.Authorization.ToArray());
        }

        if (incoming.Cookies.TryGetValue(
                AuthSecurityConstants.AccessCookieName,
                out var accessToken)
            && !string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                $"{AuthSecurityConstants.AccessCookieName}={accessToken}");
        }
    }
}
