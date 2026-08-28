using System.Net.Http.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;

namespace ResearchTrack.ProjectService.Infrastructure;

public sealed class AuthUserDirectoryClient
    : IAuthUserDirectoryClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthUserDirectoryClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AuthDirectoryUser> GetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "api/v1/users/me");

        ForwardAuthentication(request);

        using var response =
            await SendAsync(
                request,
                cancellationToken);

        var envelope =
            await response.Content
                .ReadFromJsonAsync<
                    ApiResponse<AuthDirectoryUser>>(
                        cancellationToken);

        if (!response.IsSuccessStatusCode ||
            envelope?.Success != true ||
            envelope.Data is null)
        {
            throw DependencyFailure(
                "Unable to resolve the authenticated user from Auth Service.");
        }

        return envelope.Data;
    }

    public async Task<IReadOnlyList<AuthDirectoryUser>>
        ResolveStudentsAsync(
            IReadOnlyCollection<Guid> studentIds,
            CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/users/students/resolve")
            {
                Content = JsonContent.Create(
                    new
                    {
                        studentIds
                    })
            };

        ForwardAuthentication(request);

        using var response =
            await SendAsync(
                request,
                cancellationToken);

        var envelope =
            await response.Content
                .ReadFromJsonAsync<
                    ApiResponse<
                        IReadOnlyList<AuthDirectoryUser>>>(
                        cancellationToken);

        if (!response.IsSuccessStatusCode ||
            envelope?.Success != true ||
            envelope.Data is null)
        {
            throw DependencyFailure(
                "Unable to validate selected students with Auth Service.");
        }

        return envelope.Data;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure(
                "Auth Service is unavailable.",
                exception);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure(
                "Auth Service did not respond in time.",
                exception);
        }
    }

    private void ForwardAuthentication(
        HttpRequestMessage request)
    {
        var incoming =
            _httpContextAccessor.HttpContext?.Request
            ?? throw DependencyFailure(
                "Request authentication context is unavailable.");

        if (incoming.Headers.Authorization.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                incoming.Headers.Authorization.ToArray());
        }

        if (incoming.Cookies.TryGetValue(
                AuthSecurityConstants.AccessCookieName,
                out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                $"{AuthSecurityConstants.AccessCookieName}={accessToken}");
        }
    }

    private static ApiException DependencyFailure(
        string message,
        Exception? innerException = null)
    {
        return new ApiException(
            StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.DependencyUnavailable,
            message,
            innerException: innerException);
    }
}
