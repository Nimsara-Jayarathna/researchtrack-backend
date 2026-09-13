using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure;

public sealed class GitHubPublicRepositoryClient : IGitHubPublicRepositoryClient
{
    public static readonly Uri TrustedBaseAddress = new("https://api.github.com/", UriKind.Absolute);
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubPublicRepositoryClient> _logger;

    public GitHubPublicRepositoryClient(
        HttpClient httpClient,
        ILogger<GitHubPublicRepositoryClient> logger)
    {
        if (httpClient.BaseAddress != TrustedBaseAddress)
        {
            throw new InvalidOperationException("The GitHub client must use the trusted GitHub API base address.");
        }

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GitHubPublicRepository> GetAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(
                $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}",
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("GitHub is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub did not respond in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "The public GitHub repository was not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var providerMessage = await ReadProviderMessageAsync(response, cancellationToken);
                LogValidationFailure(owner, repository, response, providerMessage);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    if (IsRateLimited(response, providerMessage))
                    {
                        throw DependencyFailure(
                            "GitHub API rate limit was reached. Retry after the GitHub rate limit resets.");
                    }

                    throw DependencyFailure(
                        "GitHub denied the public repository validation request. Verify that the repository is public and reachable from GitHub.");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw DependencyFailure(
                        "GitHub API rate limit was reached. Retry after the GitHub rate limit resets.");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw DependencyFailure(
                        "GitHub rejected the repository validation request. Public repository validation does not require a ResearchTrack access token.");
                }

                if ((int)response.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    throw DependencyFailure("GitHub is temporarily unavailable.");
                }

                throw DependencyFailure(
                    $"GitHub repository validation failed with status {(int)response.StatusCode}.");
            }

            GitHubRepositoryPayload? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<GitHubRepositoryPayload>(cancellationToken);
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw DependencyFailure("GitHub returned an invalid repository response.", exception);
            }

            if (payload is null
                || payload.Id <= 0
                || string.IsNullOrWhiteSpace(payload.Name)
                || string.IsNullOrWhiteSpace(payload.FullName)
                || string.IsNullOrWhiteSpace(payload.HtmlUrl)
                || string.IsNullOrWhiteSpace(payload.Owner?.Login)
                || string.IsNullOrWhiteSpace(payload.Owner.Type))
            {
                throw DependencyFailure("GitHub returned incomplete repository metadata.");
            }

            if (payload.Private || !IsCanonicalPublicUrl(payload.HtmlUrl))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.BadRequest,
                    "The GitHub repository must be public.");
            }

            return new GitHubPublicRepository(
                payload.Id,
                payload.Owner.Login,
                MapOwnerType(payload.Owner.Type),
                payload.Name,
                payload.FullName,
                payload.HtmlUrl,
                payload.DefaultBranch);
        }
    }

    private static bool IsCanonicalPublicUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort;

    private static string MapOwnerType(string value) =>
        value.Equals("Organization", StringComparison.OrdinalIgnoreCase)
            ? "ORG"
            : value.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? "USER"
                : throw DependencyFailure("GitHub returned an unsupported repository owner type.");

    private void LogValidationFailure(
        string owner,
        string repository,
        HttpResponseMessage response,
        string? providerMessage)
    {
        _logger.LogWarning(
            "GitHub public repository validation failed. Owner={Owner} Repository={Repository} Status={StatusCode} RateRemaining={RateRemaining} RateReset={RateReset} RetryAfter={RetryAfter} ProviderMessage={ProviderMessage}",
            owner,
            repository,
            (int)response.StatusCode,
            TryGetHeader(response, "X-RateLimit-Remaining"),
            TryGetHeader(response, "X-RateLimit-Reset"),
            TryGetHeader(response, "Retry-After"),
            providerMessage);
    }

    private static bool IsRateLimited(HttpResponseMessage response, string? providerMessage)
    {
        if (string.Equals(TryGetHeader(response, "X-RateLimit-Remaining"), "0", StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(providerMessage)
            && (providerMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || providerMessage.Contains("abuse detection", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadProviderMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<GitHubErrorPayload>(cancellationToken);
            return string.IsNullOrWhiteSpace(payload?.Message)
                ? null
                : payload.Message.Trim();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);

    private sealed record GitHubRepositoryPayload(
        long Id,
        string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("default_branch")] string? DefaultBranch,
        bool Private,
        GitHubOwnerPayload Owner);

    private sealed record GitHubOwnerPayload(string Login, string Type);

    private sealed record GitHubErrorPayload(string? Message);
}
