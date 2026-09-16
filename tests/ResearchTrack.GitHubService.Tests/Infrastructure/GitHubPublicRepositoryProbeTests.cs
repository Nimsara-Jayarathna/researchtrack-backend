using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Infrastructure;

public sealed class GitHubPublicRepositoryProbeTests
{
    [Fact]
    public async Task Public_repository_is_verified_through_git_smart_http_without_rest_api()
    {
        var advertisement = "001e# service=git-upload-pack\n0000010dabc123 HEAD\0multi_ack symref=HEAD:refs/heads/main agent=git/github\n0000";
        var handler = new StubHandler(
            HttpStatusCode.OK,
            advertisement,
            "application/x-git-upload-pack-advertisement");
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(
            "OpenAI",
            "example",
            TestContext.Current.CancellationToken);

        Assert.Equal("OpenAI/example", result.FullName);
        Assert.Equal("main", result.DefaultBranch);
        Assert.Equal("https://github.com/OpenAI/example", result.HtmlUrl);
        Assert.Equal(
            "https://github.com/OpenAI/example.git/info/refs?service=git-upload-pack",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.DoesNotContain("api.github.com", handler.LastRequestUri?.Host ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Missing_or_non_public_repository_is_rejected(HttpStatusCode statusCode)
    {
        var probe = CreateProbe(new StubHandler(statusCode, string.Empty, "text/plain"));

        var exception = await Assert.ThrowsAsync<ApiException>(() => probe.ProbeAsync(
            "owner",
            "repository",
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Contains("public", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_failure_is_dependency_unavailable_not_repository_validation_rate_limit()
    {
        var probe = CreateProbe(new StubHandler(
            HttpStatusCode.BadGateway,
            "upstream failure",
            "text/plain"));

        var exception = await Assert.ThrowsAsync<ApiException>(() => probe.ProbeAsync(
            "owner",
            "repository",
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.DoesNotContain("rate limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubPublicRepositoryProbe CreateProbe(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = GitHubPublicRepositoryProbe.TrustedBaseAddress
        };
        return new GitHubPublicRepositoryProbe(
            client,
            NullLogger<GitHubPublicRepositoryProbe>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        private readonly string _mediaType;

        public StubHandler(HttpStatusCode statusCode, string content, string mediaType)
        {
            _statusCode = statusCode;
            _content = content;
            _mediaType = mediaType;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(_content));
            content.Headers.ContentType = new MediaTypeHeaderValue(_mediaType);
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = content,
                RequestMessage = request
            });
        }
    }
}
