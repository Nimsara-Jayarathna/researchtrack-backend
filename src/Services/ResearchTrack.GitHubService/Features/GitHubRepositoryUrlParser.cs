using System.Text.RegularExpressions;
using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.GitHubService.Features;

public static partial class GitHubRepositoryUrlParser
{
    public static GitHubRepositoryAddress Parse(string? value)
    {
        var input = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            throw InvalidUrl();
        }

        if (!input.Contains("://", StringComparison.Ordinal))
        {
            input = $"https://{input}";
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath.Contains('%', StringComparison.Ordinal))
        {
            throw InvalidUrl();
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw InvalidUrl();
        }

        var owner = segments[0];
        var repository = GitSuffixRegex().Replace(segments[1], string.Empty);
        if (!OwnerRegex().IsMatch(owner) || !RepositoryRegex().IsMatch(repository))
        {
            throw InvalidUrl();
        }

        return new GitHubRepositoryAddress(
            owner,
            repository,
            $"https://github.com/{owner}/{repository}");
    }

    private static ApiValidationException InvalidUrl() => new(
        [new ApiFieldError(
            "repositoryUrl",
            ["Enter a valid GitHub repository URL in the form https://github.com/owner/repository."])]);

    [GeneratedRegex("\\.git$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitSuffixRegex();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryRegex();
}
