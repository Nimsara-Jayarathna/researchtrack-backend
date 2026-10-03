using System.Net;
using System.Net.Http.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.MeetingService.Domain;

namespace ResearchTrack.MeetingService.Infrastructure;

public sealed class UserProfileClient : IUserProfileClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserProfileClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> GetCurrentUserDisplayNameAsync(
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/users/me");
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
                "Auth Service is unavailable.",
                innerException: exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new ApiException(
                    StatusCodes.Status401Unauthorized,
                    ErrorCodes.Unauthorized,
                    "Authentication is required.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DependencyUnavailable,
                    "Unable to resolve the authenticated user profile.");
            }

            ApiResponse<UserProfileResponse>? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<ApiResponse<UserProfileResponse>>(
                        cancellationToken);
            }
            catch (Exception exception) when (
                exception is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DependencyUnavailable,
                    "Auth Service returned an invalid user profile response.",
                    innerException: exception);
            }

            var profile = payload?.Data;
            if (profile is null)
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DependencyUnavailable,
                    "Auth Service returned no user profile.");
            }

            var displayName =
                $"{profile.FirstName?.Trim()} {profile.LastName?.Trim()}".Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = profile.Email?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DependencyUnavailable,
                    "The authenticated user profile does not contain a display name.");
            }

            return displayName.Length <= MeetingChannelConstants.UserDisplayNameMaxLength
                ? displayName
                : displayName[..MeetingChannelConstants.UserDisplayNameMaxLength];
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

    private sealed record UserProfileResponse(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        string? RegistrationNumber,
        string Role);
}
