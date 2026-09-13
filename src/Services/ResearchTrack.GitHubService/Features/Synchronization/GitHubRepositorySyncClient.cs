using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class GitHubRepositorySyncClient : IGitHubRepositorySyncClient
{
    private const int PerPage = 100;
    private const int MaxPages = 1000;
    private readonly HttpClient _httpClient;

    public GitHubRepositorySyncClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GitHubSyncRepository> GetRepositoryAsync(
        string owner,
        string repository,
        string? token,
        CancellationToken cancellationToken)
    {
        using var payload = await GetAsync($"repos/{Part(owner)}/{Part(repository)}", token, cancellationToken);
        return ParseRepository(payload.RootElement);
    }

    public Task<IReadOnlyList<GitHubSyncBranch>> GetBranchesAsync(
        string owner,
        string repository,
        string? token,
        CancellationToken cancellationToken) =>
        GetPagedAsync($"repos/{Part(owner)}/{Part(repository)}/branches", token, ParseBranch, cancellationToken);

    public Task<IReadOnlyList<GitHubSyncCommit>> GetCommitsAsync(
        string owner,
        string repository,
        string branch,
        string? token,
        CancellationToken cancellationToken) =>
        GetPagedAsync(
            $"repos/{Part(owner)}/{Part(repository)}/commits?sha={Uri.EscapeDataString(branch)}",
            token,
            ParseCommit,
            cancellationToken);

    public async Task<GitHubSyncCommit> GetCommitAsync(
        string owner,
        string repository,
        string sha,
        string? token,
        CancellationToken cancellationToken)
    {
        using var payload = await GetAsync(
            $"repos/{Part(owner)}/{Part(repository)}/commits/{Part(sha)}",
            token,
            cancellationToken);
        return ParseCommit(payload.RootElement);
    }

    public Task<IReadOnlyList<GitHubSyncContributor>> GetContributorsAsync(
        string owner,
        string repository,
        string? token,
        CancellationToken cancellationToken) =>
        GetPagedAsync(
            $"repos/{Part(owner)}/{Part(repository)}/contributors?anon=0",
            token,
            ParseContributor,
            cancellationToken);

    public Task<IReadOnlyList<GitHubSyncPullRequest>> GetPullRequestsAsync(
        string owner,
        string repository,
        string? token,
        CancellationToken cancellationToken) =>
        GetPagedAsync(
            $"repos/{Part(owner)}/{Part(repository)}/pulls?state=all&sort=updated&direction=desc",
            token,
            ParsePullRequest,
            cancellationToken);

    public async Task<GitHubSyncPullRequest> GetPullRequestAsync(
        string owner,
        string repository,
        int number,
        string? token,
        CancellationToken cancellationToken)
    {
        using var payload = await GetAsync(
            $"repos/{Part(owner)}/{Part(repository)}/pulls/{number}",
            token,
            cancellationToken);
        return ParsePullRequest(payload.RootElement);
    }

    public Task<IReadOnlyList<GitHubSyncReview>> GetPullRequestReviewsAsync(
        string owner,
        string repository,
        int number,
        string? token,
        CancellationToken cancellationToken) =>
        GetPagedAsync(
            $"repos/{Part(owner)}/{Part(repository)}/pulls/{number}/reviews",
            token,
            ParseReview,
            cancellationToken);

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(
        string path,
        string? token,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var separator = path.Contains('?') ? '&' : '?';
            using var payload = await GetAsync(
                $"{path}{separator}per_page={PerPage}&page={page}",
                token,
                cancellationToken);

            if (payload.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw DependencyFailure("GitHub returned an invalid paginated response.");
            }

            var count = 0;
            foreach (var element in payload.RootElement.EnumerateArray())
            {
                items.Add(parser(element));
                count++;
            }

            if (count < PerPage)
            {
                break;
            }
        }

        return items;
    }

    private async Task<JsonDocument> GetAsync(
        string relativeUrl,
        string? token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

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
            throw DependencyFailure("GitHub API is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw DependencyFailure("GitHub API did not respond in time.", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ApiException(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "The GitHub repository or resource was not found.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ApiException(
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.Forbidden,
                    "ResearchTrack no longer has permission to read this GitHub repository.");
            }

            if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                throw new ApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    ErrorCodes.DependencyUnavailable,
                    "GitHub API rate limit was reached. Retry after the GitHub rate limit resets.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw DependencyFailure(
                    $"GitHub API returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private static GitHubSyncRepository ParseRepository(JsonElement element)
    {
        var owner = RequiredObject(element, "owner");
        return new GitHubSyncRepository(
            RequiredInt64(element, "id"),
            RequiredString(owner, "login"),
            RequiredString(element, "name"),
            RequiredString(element, "full_name"),
            RequiredString(element, "html_url"),
            RequiredString(element, "default_branch"),
            Bool(element, "private"),
            Bool(element, "archived"),
            Bool(element, "fork"),
            NullableString(element, "description"),
            NullableDate(element, "created_at"),
            NullableDate(element, "updated_at"),
            NullableDate(element, "pushed_at"));
    }

    private static GitHubSyncBranch ParseBranch(JsonElement element)
    {
        var commit = RequiredObject(element, "commit");
        return new GitHubSyncBranch(
            RequiredString(element, "name"),
            RequiredString(commit, "sha"),
            Bool(element, "protected"));
    }

    private static GitHubSyncCommit ParseCommit(JsonElement element)
    {
        var commit = RequiredObject(element, "commit");
        var authorInfo = RequiredObject(commit, "author");
        var committerInfo = RequiredObject(commit, "committer");
        var author = OptionalObject(element, "author");
        var committer = OptionalObject(element, "committer");
        var parentCount = element.TryGetProperty("parents", out var parents)
            && parents.ValueKind == JsonValueKind.Array
                ? parents.GetArrayLength()
                : 0;

        int? additions = null;
        int? deletions = null;
        int? changedFiles = null;
        if (element.TryGetProperty("stats", out var stats)
            && stats.ValueKind == JsonValueKind.Object)
        {
            additions = NullableInt(stats, "additions");
            deletions = NullableInt(stats, "deletions");
        }

        if (element.TryGetProperty("files", out var files)
            && files.ValueKind == JsonValueKind.Array)
        {
            changedFiles = files.GetArrayLength();
        }

        return new GitHubSyncCommit(
            RequiredString(element, "sha"),
            RequiredString(commit, "message"),
            author is null ? null : NullableInt64(author.Value, "id"),
            author is null ? null : NullableString(author.Value, "login"),
            NullableString(authorInfo, "name"),
            NullableString(authorInfo, "email"),
            committer is null ? null : NullableInt64(committer.Value, "id"),
            committer is null ? null : NullableString(committer.Value, "login"),
            NullableDate(authorInfo, "date"),
            NullableDate(committerInfo, "date"),
            RequiredString(element, "html_url"),
            parentCount,
            additions,
            deletions,
            changedFiles);
    }

    private static GitHubSyncContributor ParseContributor(JsonElement element) => new(
        RequiredInt64(element, "id"),
        RequiredString(element, "login"),
        NullableString(element, "avatar_url"),
        NullableString(element, "html_url"),
        NullableInt(element, "contributions") ?? 0);

    private static GitHubSyncPullRequest ParsePullRequest(JsonElement element)
    {
        var user = OptionalObject(element, "user");
        var head = RequiredObject(element, "head");
        var target = RequiredObject(element, "base");

        return new GitHubSyncPullRequest(
            RequiredInt64(element, "id"),
            RequiredInt(element, "number"),
            RequiredString(element, "title"),
            NullableString(element, "body"),
            RequiredString(element, "state").ToUpperInvariant(),
            Bool(element, "draft"),
            Bool(element, "merged") || NullableDate(element, "merged_at") is not null,
            user is null ? null : NullableInt64(user.Value, "id"),
            user is null ? null : NullableString(user.Value, "login"),
            RequiredString(head, "ref"),
            RequiredString(head, "sha"),
            RequiredString(target, "ref"),
            RequiredString(target, "sha"),
            RequiredDate(element, "created_at"),
            RequiredDate(element, "updated_at"),
            NullableDate(element, "closed_at"),
            NullableDate(element, "merged_at"),
            NullableString(element, "merge_commit_sha"),
            RequiredString(element, "html_url"),
            NullableInt(element, "additions"),
            NullableInt(element, "deletions"),
            NullableInt(element, "changed_files"),
            NullableInt(element, "commits"),
            NullableInt(element, "comments"),
            NullableInt(element, "review_comments"));
    }

    private static GitHubSyncReview ParseReview(JsonElement element)
    {
        var user = OptionalObject(element, "user");
        return new GitHubSyncReview(
            RequiredInt64(element, "id"),
            user is null ? null : NullableInt64(user.Value, "id"),
            user is null ? null : NullableString(user.Value, "login"),
            RequiredString(element, "state").ToUpperInvariant(),
            NullableDate(element, "submitted_at"));
    }

    private static string Part(string value) => Uri.EscapeDataString(value.Trim());

    private static JsonElement RequiredObject(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        throw DependencyFailure($"GitHub payload is missing '{propertyName}'.");
    }

    private static JsonElement? OptionalObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string RequiredString(JsonElement element, string propertyName) =>
        NullableString(element, propertyName)
        ?? throw DependencyFailure($"GitHub payload is missing '{propertyName}'.");

    private static string? NullableString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long RequiredInt64(JsonElement element, string propertyName) =>
        NullableInt64(element, propertyName)
        ?? throw DependencyFailure($"GitHub payload is missing '{propertyName}'.");

    private static long? NullableInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.TryGetInt64(out var number)
            ? number
            : null;

    private static int RequiredInt(JsonElement element, string propertyName) =>
        NullableInt(element, propertyName)
        ?? throw DependencyFailure($"GitHub payload is missing '{propertyName}'.");

    private static int? NullableInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool Bool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static DateTime RequiredDate(JsonElement element, string propertyName) =>
        NullableDate(element, propertyName)
        ?? throw DependencyFailure($"GitHub payload is missing '{propertyName}'.");

    private static DateTime? NullableDate(JsonElement element, string propertyName)
    {
        var raw = NullableString(element, propertyName);
        return raw is not null && DateTimeOffset.TryParse(raw, out var value)
            ? value.UtcDateTime
            : null;
    }

    private static ApiException DependencyFailure(string message, Exception? inner = null) => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        message,
        innerException: inner);
}
