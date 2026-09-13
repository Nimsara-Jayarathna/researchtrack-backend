using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubUserAuthorizationClient : IGitHubUserAuthorizationClient
{
    public static readonly Uri TrustedBaseAddress = new("https://github.com/", UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly GitHubAppOptions _options;

    public GitHubUserAuthorizationClient(HttpClient httpClient, GitHubAppOptions options)
    {
        if (httpClient.BaseAddress != TrustedBaseAddress)
        {
            throw new InvalidOperationException(
                "The GitHub user authorization client must use the trusted GitHub web base address.");
        }

        _httpClient = httpClient;
        _options = options;
    }

    public async Task<GitHubUserAccessToken> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("GitHub authorization code is required.", nameof(code));
        }
        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            throw new ArgumentException("GitHub PKCE verifier is required.", nameof(codeVerifier));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _options.SetupCallbackUrl.AbsoluteUri,
                ["code_verifier"] = codeVerifier
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("GitHub user authorization is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub user authorization did not respond in time.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure("GitHub rejected the user authorization token request.");
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken);
            if (payload is null
                || !string.IsNullOrWhiteSpace(payload.Error)
                || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationError,
                    "GitHub user authorization could not be completed.");
            }

            return new GitHubUserAccessToken(payload.AccessToken);
        }
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error);
}
