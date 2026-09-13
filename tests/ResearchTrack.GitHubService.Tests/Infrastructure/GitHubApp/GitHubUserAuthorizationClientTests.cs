using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubUserAuthorizationClientTests
{
    [Fact]
    public async Task Exchanges_code_with_backend_only_client_secret_and_pkce_verifier()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{\"access_token\":\"ghu_ephemeral\"}");
        var client = new GitHubUserAuthorizationClient(
            new HttpClient(handler) { BaseAddress = GitHubUserAuthorizationClient.TrustedBaseAddress }, Options());

        var token = await client.ExchangeCodeAsync("code", "verifier", TestContext.Current.CancellationToken);

        Assert.Equal("ghu_ephemeral", token.Value);
        Assert.Equal("https://github.com/login/oauth/access_token", handler.Uri?.AbsoluteUri);
        Assert.Contains("client_id=Iv1.test-client", handler.Body);
        Assert.Contains("code_verifier=verifier", handler.Body);
        Assert.Contains("client_secret=test-client-secret", handler.Body);
    }

    [Fact]
    public async Task Timeout_is_returned_as_safe_dependency_failure()
    {
        var client = new GitHubUserAuthorizationClient(
            new HttpClient(new TimeoutHandler()) { BaseAddress = GitHubUserAuthorizationClient.TrustedBaseAddress }, Options());

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.ExchangeCodeAsync(
            "code", "verifier", TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.DoesNotContain("test-client-secret", exception.Message, StringComparison.Ordinal);
    }

    private static GitHubAppOptions Options() => new(
        12345, "researchtrack-test", "Iv1.test-client", "test-client-secret", "/tmp/key.pem",
        new Uri("https://api.example.test/api/github/access-source/install/callback"),
        new Uri("https://app.example.test/"), TimeSpan.FromMinutes(10));

    private sealed class CapturingHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
    }
}
