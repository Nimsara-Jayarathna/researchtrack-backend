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

    public GitHubPublicRepositoryClient(HttpClient httpClient)
    {
        if (httpClient.BaseAddress != TrustedBaseAddress)
        {
            throw new InvalidOperationException("The GitHub client must use the trusted GitHub API base address.");
        }

        _httpClient = httpClient;
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
                throw DependencyFailure("GitHub could not validate the repository.");
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
}
