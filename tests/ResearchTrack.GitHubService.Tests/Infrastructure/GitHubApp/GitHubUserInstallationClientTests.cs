using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubUserInstallationClientTests
{
    [Fact]
    public async Task Finds_exact_installation_in_user_accessible_installation_inventory()
    {
        var handler = new JsonHandler(HttpStatusCode.OK, """
            {"total_count":2,"installations":[{"id":123},{"id":98765}]}
            """);
        var client = CreateClient(handler);

        var result = await client.CanAccessInstallationAsync(
            new GitHubUserAccessToken("ghu_ephemeral"), 98765, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("https://api.github.com/user/installations?per_page=100&page=1", handler.Uri?.AbsoluteUri);
    }

    [Fact]
    public async Task Installation_absent_from_user_inventory_is_rejected()
    {
        var client = CreateClient(new JsonHandler(
            HttpStatusCode.OK,
            "{\"total_count\":1,\"installations\":[{\"id\":123}]}"));

        var result = await client.CanAccessInstallationAsync(
            new GitHubUserAccessToken("ghu_ephemeral"), 98765, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Unauthorized_or_forbidden_user_token_never_counts_as_verified(HttpStatusCode status)
    {
        var client = CreateClient(new JsonHandler(status, "{}"));

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.CanAccessInstallationAsync(
            new GitHubUserAccessToken("ghu_ephemeral"), 98765, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Timeout_is_a_safe_dependency_failure()
    {
        var client = new GitHubUserInstallationClient(
            new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://api.github.com/") });

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.CanAccessInstallationAsync(
            new GitHubUserAccessToken("ghu_ephemeral"), 98765, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    private static GitHubUserInstallationClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    private sealed class JsonHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
    }
}
