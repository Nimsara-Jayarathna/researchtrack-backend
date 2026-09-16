using System.Net;
using System.Text;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure;

/// <summary>
/// Verifies that a GitHub repository is anonymously cloneable without consuming
/// the GitHub REST API rate limit. Git's smart HTTP discovery endpoint is used
/// instead of api.github.com.
/// </summary>
public sealed class GitHubPublicRepositoryProbe : IGitHubPublicRepositoryProbe
{
    public static readonly Uri TrustedBaseAddress = new("https://github.com/");

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubPublicRepositoryProbe> _logger;

    public GitHubPublicRepositoryProbe(
        HttpClient httpClient,
        ILogger<GitHubPublicRepositoryProbe> logger)
    {
        if (httpClient.BaseAddress != TrustedBaseAddress)
        {
            throw new InvalidOperationException("The GitHub public repository probe must use the trusted github.com base address.");
        }

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<GitHubPublicRepositoryProbeResult> ProbeAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"{Part(owner)}/{Part(repository)}.git/info/refs?service=git-upload-pack";
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw DependencyFailure("GitHub could not be reached to verify the public repository.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub did not respond in time while verifying the public repository.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "The GitHub repository was not found or is not publicly accessible.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub smart HTTP public repository probe failed. Owner={Owner} Repository={Repository} Status={StatusCode}",
                    owner,
                    repository,
                    (int)response.StatusCode);
                throw DependencyFailure(
                    $"GitHub could not verify the public repository (HTTP {(int)response.StatusCode}).");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(
                    mediaType,
                    "application/x-git-upload-pack-advertisement",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "GitHub smart HTTP probe returned an unexpected content type. Owner={Owner} Repository={Repository} ContentType={ContentType}",
                    owner,
                    repository,
                    mediaType ?? "(null)");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                throw DependencyFailure("GitHub returned an empty response while verifying the public repository.");
            }

            var defaultBranch = TryReadDefaultBranch(bytes);
            var canonicalOwner = owner.Trim();
            var canonicalRepository = repository.Trim();
            return new GitHubPublicRepositoryProbeResult(
                canonicalOwner,
                canonicalRepository,
                $"{canonicalOwner}/{canonicalRepository}",
                $"https://github.com/{canonicalOwner}/{canonicalRepository}",
                defaultBranch);
        }
    }

    private static string? TryReadDefaultBranch(byte[] advertisement)
    {
        // The v0/v1 advertisement normally includes
        // "symref=HEAD:refs/heads/<branch>" in the capability list. If GitHub
        // responds using protocol v2, default branch enrichment can happen on the
        // first REST synchronization and a null branch is safe here.
        var text = Encoding.UTF8.GetString(advertisement);
        const string marker = "symref=HEAD:refs/heads/";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = start;
        while (end < text.Length)
        {
            var character = text[end];
            if (character is '\0' or '\n' or '\r' or ' ')
            {
                break;
            }
            end++;
        }

        return end > start ? text[start..end] : null;
    }

    private static string Part(string value) => Uri.EscapeDataString(value.Trim());

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
