using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubInstallationApiClient : IGitHubInstallationApiClient
{
    private readonly HttpClient _httpClient;

    public GitHubInstallationApiClient(HttpClient httpClient)
    {
        if (httpClient.BaseAddress != GitHubPublicRepositoryClient.TrustedBaseAddress)
        {
            throw new InvalidOperationException("The GitHub installation client must use the trusted GitHub API base address.");
        }

        _httpClient = httpClient;
    }

    public async Task<JsonDocument> GetAsync(
        string relativePath,
        GitHubInstallationToken token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _)
            || relativePath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("A relative GitHub API path is required.", nameof(relativePath));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("GitHub installation API is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub installation API did not respond in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "The requested repository is not accessible to this GitHub App installation.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure("GitHub rejected the installation API request.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
