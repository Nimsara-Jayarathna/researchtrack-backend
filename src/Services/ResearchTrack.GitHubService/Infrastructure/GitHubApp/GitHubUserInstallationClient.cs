using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubUserInstallationClient : IGitHubUserInstallationClient
{
    private const int PageSize = 100;
    private const int MaxPages = 100;
    private readonly HttpClient _httpClient;

    public GitHubUserInstallationClient(HttpClient httpClient)
    {
        if (httpClient.BaseAddress != GitHubPublicRepositoryClient.TrustedBaseAddress)
        {
            throw new InvalidOperationException(
                "The GitHub user installation client must use the trusted GitHub API base address.");
        }

        _httpClient = httpClient;
    }

    public async Task<bool> CanAccessInstallationAsync(
        GitHubUserAccessToken token,
        long installationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.Value))
        {
            throw new ArgumentException("GitHub user access token is required.", nameof(token));
        }
        if (installationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(installationId));
        }

        // GitHub's setup-URL guidance requires correlating the returned installation_id with the
        // user who installed the App. /user/installations is the direct user-to-installation check
        // and requires no extra App permission beyond a valid GitHub App user access token.
        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"user/installations?per_page={PageSize}&page={page}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

            using var response = await SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ApiException(
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.Forbidden,
                    "GitHub could not confirm access to the selected App installation.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure("GitHub rejected the user installation verification request.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = payload.RootElement;
            if (!root.TryGetProperty("total_count", out var totalElement)
                || !totalElement.TryGetInt32(out var totalCount)
                || !root.TryGetProperty("installations", out var installations)
                || installations.ValueKind != JsonValueKind.Array)
            {
                throw DependencyFailure("GitHub returned invalid user installation metadata.");
            }

            foreach (var installation in installations.EnumerateArray())
            {
                if (installation.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt64(out var id)
                    && id == installationId)
                {
                    return true;
                }
            }

            if (page * PageSize >= totalCount || installations.GetArrayLength() == 0)
            {
                return false;
            }
        }

        throw DependencyFailure("GitHub user installation pagination exceeded the supported limit.");
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
            throw DependencyFailure("GitHub user installation verification is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub user installation verification did not respond in time.", exception);
        }
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
