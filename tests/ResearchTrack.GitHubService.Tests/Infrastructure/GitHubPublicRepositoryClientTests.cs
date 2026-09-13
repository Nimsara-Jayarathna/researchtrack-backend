using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Infrastructure;

public sealed class GitHubPublicRepositoryClientTests
{
    [Fact]
    public async Task Maps_valid_public_repository_from_trusted_api()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PublicRepositoryJson());
        var client = CreateClient(handler);

        var result = await client.GetAsync("OpenAI", "example", TestContext.Current.CancellationToken);

        Assert.Equal(1296269, result.Id);
        Assert.Equal("openai", result.OwnerLogin);
        Assert.Equal("ORG", result.OwnerType);
        Assert.Equal("openai/example", result.FullName);
        Assert.Equal("main", result.DefaultBranch);
        Assert.Equal("https://github.com/openai/example", result.HtmlUrl);
        Assert.Equal(
            "https://api.github.com/repos/OpenAI/example",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Maps_github_404_to_repository_not_found()
    {
        var client = CreateClient(new StubHandler(HttpStatusCode.NotFound, "{}"));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "missing", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "temporarily unavailable")]
    public async Task Maps_github_failures_to_actionable_dependency_errors(
        HttpStatusCode upstreamStatus,
        string expectedMessage)
    {
        var client = CreateClient(new StubHandler(upstreamStatus, "{}"));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "repository", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Maps_github_403_rate_limit_only_when_provider_reports_rate_limit()
    {
        var client = CreateClient(new StubHandler(
            HttpStatusCode.Forbidden,
            "{\"message\":\"API rate limit exceeded for this IP.\"}"));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "repository", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Contains("rate limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Does_not_misreport_generic_github_403_as_rate_limit()
    {
        var client = CreateClient(new StubHandler(
            HttpStatusCode.Forbidden,
            "{\"message\":\"Repository access blocked.\"}"));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "repository", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Contains("denied", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rate limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Maps_rate_limit_and_upstream_errors_to_dependency_unavailable(
        HttpStatusCode upstreamStatus)
    {
        var client = CreateClient(new StubHandler(upstreamStatus, "{}"));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "repository", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal("DEPENDENCY_UNAVAILABLE", exception.Code);
    }

    [Fact]
    public async Task Explicitly_rejects_private_repository_payload()
    {
        var client = CreateClient(new StubHandler(
            HttpStatusCode.OK,
            PublicRepositoryJson().Replace("\"private\": false", "\"private\": true", StringComparison.Ordinal)));

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.GetAsync("owner", "private", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("public", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubPublicRepositoryClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = GitHubPublicRepositoryClient.TrustedBaseAddress
        };
        return new GitHubPublicRepositoryClient(
            httpClient,
            NullLogger<GitHubPublicRepositoryClient>.Instance);
    }

    private static string PublicRepositoryJson() => """
        {
          "id": 1296269,
          "name": "example",
          "full_name": "openai/example",
          "private": false,
          "owner": { "login": "openai", "type": "Organization" },
          "html_url": "https://github.com/openai/example",
          "default_branch": "main"
        }
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StubHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }
}
