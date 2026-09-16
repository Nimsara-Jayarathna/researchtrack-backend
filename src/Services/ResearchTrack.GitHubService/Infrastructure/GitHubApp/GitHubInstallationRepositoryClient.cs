using System.Text.Json;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public sealed class GitHubInstallationRepositoryClient : IGitHubInstallationRepositoryClient
{
    private readonly IGitHubInstallationApiClient _apiClient;

    public GitHubInstallationRepositoryClient(IGitHubInstallationApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<GitHubInstallationRepositoryPage> ListAsync(
        GitHubInstallationToken token,
        int page,
        int perPage,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
        if (perPage is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(perPage));
        }

        using var payload = await _apiClient.GetAsync(
            $"installation/repositories?per_page={perPage}&page={page}",
            token,
            cancellationToken);

        if (!payload.RootElement.TryGetProperty("total_count", out var totalElement)
            || !totalElement.TryGetInt32(out var totalCount)
            || totalCount < 0
            || !payload.RootElement.TryGetProperty("repositories", out var repositoriesElement)
            || repositoriesElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidGitHubPayload();
        }

        var repositories = repositoriesElement
            .EnumerateArray()
            .Select(ParseRepository)
            .ToList();

        return new GitHubInstallationRepositoryPage(
            repositories,
            totalCount,
            page * perPage < totalCount && repositories.Count > 0);
    }

    public async Task<GitHubInstallationRepository> GetAsync(
        GitHubInstallationToken token,
        long repositoryId,
        CancellationToken cancellationToken)
    {
        if (repositoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repositoryId));
        }

        using var payload = await _apiClient.GetAsync(
            $"repositories/{repositoryId}",
            token,
            cancellationToken);
        var repository = ParseRepository(payload.RootElement);
        if (repository.Id != repositoryId)
        {
            throw InvalidGitHubPayload();
        }

        return repository;
    }

    private static GitHubInstallationRepository ParseRepository(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement)
            || !idElement.TryGetInt64(out var id)
            || id <= 0
            || !TryRequiredString(element, "name", out var name)
            || !TryRequiredString(element, "full_name", out var fullName)
            || !TryRequiredString(element, "html_url", out var htmlUrl)
            || !element.TryGetProperty("owner", out var owner)
            || !TryRequiredString(owner, "login", out var ownerLogin))
        {
            throw InvalidGitHubPayload();
        }

        string? defaultBranch = null;
        if (element.TryGetProperty("default_branch", out var defaultBranchElement)
            && defaultBranchElement.ValueKind == JsonValueKind.String)
        {
            defaultBranch = defaultBranchElement.GetString();
        }

        var isPrivate = element.TryGetProperty("private", out var privateElement)
            && privateElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && privateElement.GetBoolean();

        if (!Uri.TryCreate(htmlUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidGitHubPayload();
        }

        return new GitHubInstallationRepository(
            id,
            ownerLogin,
            name,
            fullName,
            uri.AbsoluteUri.TrimEnd('/'),
            defaultBranch,
            isPrivate);
    }

    private static bool TryRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static ApiException InvalidGitHubPayload() => new(
        StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.DependencyUnavailable,
        "GitHub returned invalid repository metadata.");
}
