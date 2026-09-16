using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Features;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class GitHubRepositoryUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/owner/repository/", "https://github.com/owner/repository")]
    [InlineData("https://github.com/owner/repository.git", "https://github.com/owner/repository")]
    [InlineData("github.com/owner/repository", "https://github.com/owner/repository")]
    public void Normalizes_supported_repository_urls(string input, string expected)
    {
        var parsed = GitHubRepositoryUrlParser.Parse(input);

        Assert.Equal("owner", parsed.Owner);
        Assert.Equal("repository", parsed.Repository);
        Assert.Equal(expected, parsed.NormalizedUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://github.com/owner/repository")]
    [InlineData("https://gitlab.com/owner/repository")]
    [InlineData("https://bitbucket.org/owner/repository")]
    [InlineData("https://user:password@github.com/owner/repository")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/repository/issues")]
    [InlineData("https://github.com/owner/repository/pull/1")]
    [InlineData("https://github.com/owner/repository/tree/main")]
    [InlineData("https://github.com/owner/repository/blob/main/readme.md")]
    [InlineData("https://github.com/owner/repository/settings")]
    [InlineData("https://github.com/owner/repository?tab=readme")]
    [InlineData("https://github.com/owner/repository#readme")]
    [InlineData("https://github.com:444/owner/repository")]
    [InlineData("https://github.com/owner%2Frepository")]
    public void Rejects_malformed_or_unsupported_urls(string input)
    {
        var exception = Assert.Throws<ApiValidationException>(
            () => GitHubRepositoryUrlParser.Parse(input));

        Assert.Equal("repositoryUrl", Assert.Single(exception.FieldErrors).Field);
    }
}
