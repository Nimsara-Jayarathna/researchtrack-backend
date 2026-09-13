using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubAppClient : IGitHubAppClient
{
    private readonly HttpClient _httpClient;
    private readonly IGitHubAppJwtProvider _jwtProvider;

    public GitHubAppClient(HttpClient httpClient, IGitHubAppJwtProvider jwtProvider)
    {
        if (httpClient.BaseAddress != GitHubPublicRepositoryClient.TrustedBaseAddress)
        {
            throw new InvalidOperationException("The GitHub App client must use the trusted GitHub API base address.");
        }

        _httpClient = httpClient;
        _jwtProvider = jwtProvider;
    }

    public async Task<GitHubInstallationInfo> GetInstallationAsync(
        long installationId,
        CancellationToken cancellationToken)
    {
        if (installationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installationId));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"app/installations/{installationId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtProvider.CreateToken());

        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The GitHub App installation was not found or is no longer accessible.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw DependencyFailure("GitHub rejected the installation lookup.");
        }

        var payload = await response.Content.ReadFromJsonAsync<InstallationResponse>(
            cancellationToken: cancellationToken);
        if (payload is null
            || payload.Id != installationId
            || string.IsNullOrWhiteSpace(payload.Account?.Login)
            || string.IsNullOrWhiteSpace(payload.Account.Type))
        {
            throw DependencyFailure("GitHub returned invalid installation metadata.");
        }

        var ownerType = payload.Account.Type.Equals("Organization", StringComparison.OrdinalIgnoreCase)
            ? "ORG"
            : payload.Account.Type.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "USER"
                : throw DependencyFailure("GitHub returned an unsupported installation owner type.");

        return new GitHubInstallationInfo(payload.Id, payload.Account.Login, ownerType);
    }

    public async Task<GitHubInstallationToken> CreateInstallationTokenAsync(
        long installationId,
        CancellationToken cancellationToken)
    {
        if (installationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installationId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtProvider.CreateToken());

        using (var response = await SendAsync(request, cancellationToken))
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "The GitHub App installation was not found or is no longer accessible.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure("GitHub rejected the installation token request.");
            }

            var payload = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>(
                cancellationToken: cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
            {
                throw DependencyFailure("GitHub returned an invalid installation token response.");
            }

            return new GitHubInstallationToken(payload.Token, payload.ExpiresAt.UtcDateTime);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("GitHub App API is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub App API did not respond in time.", exception);
        }
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);

    private sealed record InstallationTokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

    private sealed record InstallationResponse(long Id, InstallationAccount Account);

    private sealed record InstallationAccount(string Login, string Type);
}
