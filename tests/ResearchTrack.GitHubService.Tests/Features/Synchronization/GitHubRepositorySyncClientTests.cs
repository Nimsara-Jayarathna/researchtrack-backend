using System.Net;
using System.Text;
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Tests.Features.Synchronization;

public sealed class GitHubRepositorySyncClientTests
{
    [Fact]
    public async Task GetDefaultBranchHeadAsync_ReadsOnlyBranchHeadSha()
    {
        var handler = new StubHandler();
        var client = new GitHubRepositorySyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        var result = await client.GetDefaultBranchHeadAsync(
            "research-owner",
            "research-repo",
            "main",
            "installation-token",
            TestContext.Current.CancellationToken);

        Assert.Equal("main", result.DefaultBranch);
        Assert.Equal("0123456789abcdef", result.HeadSha);
        Assert.Equal(
            "https://api.github.com/repos/research-owner/research-repo/branches/main",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("installation-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task GetDefaultBranchHeadAsync_EncodesBranchNames()
    {
        var handler = new StubHandler();
        var client = new GitHubRepositorySyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        await client.GetDefaultBranchHeadAsync(
            "research-owner",
            "research-repo",
            "release/v2",
            "installation-token",
            TestContext.Current.CancellationToken);

        Assert.Contains("branches/release%2Fv2", handler.LastRequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"name\":\"main\",\"commit\":{\"sha\":\"0123456789abcdef\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
