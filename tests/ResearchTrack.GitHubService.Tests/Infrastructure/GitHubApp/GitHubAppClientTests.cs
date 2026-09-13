using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using System.Net;
using System.Text;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubAppClientTests
{
    [Fact]
    public async Task Installation_metadata_is_verified_with_app_jwt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"id":98765,"account":{"login":"openai","type":"Organization"}}
            """);
        var client = new GitHubAppClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") },
            new StubJwtProvider());

        var installation = await client.GetInstallationAsync(
            98765,
            TestContext.Current.CancellationToken);

        Assert.Equal(98765, installation.InstallationId);
        Assert.Equal("openai", installation.OwnerLogin);
        Assert.Equal("ORG", installation.OwnerType);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("app-jwt", handler.AuthorizationParameter);
        Assert.Equal("https://api.github.com/app/installations/98765", handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Installation_token_is_requested_with_app_jwt_and_kept_server_side()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """
            {"token":"installation-secret","expires_at":"2026-09-12T06:30:00Z"}
            """);
        var client = new GitHubAppClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") },
            new StubJwtProvider());

        var token = await client.CreateInstallationTokenAsync(
            98765,
            TestContext.Current.CancellationToken);

        Assert.Equal("installation-secret", token.Value);
        Assert.Equal(new DateTime(2026, 9, 12, 6, 30, 0, DateTimeKind.Utc), token.ExpiresAt);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("app-jwt", handler.AuthorizationParameter);
        Assert.Equal("https://api.github.com/app/installations/98765/access_tokens", handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Missing_installation_is_reported_as_not_found()
    {
        var client = new GitHubAppClient(
            new HttpClient(new StubHandler(HttpStatusCode.NotFound, "{}"))
            {
                BaseAddress = new Uri("https://api.github.com/")
            },
            new StubJwtProvider());

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetInstallationAsync(
            98765,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Installation_token_request_failure_is_safe_dependency_failure(HttpStatusCode status)
    {
        var client = new GitHubAppClient(
            new HttpClient(new StubHandler(status, "{}")) { BaseAddress = new Uri("https://api.github.com/") },
            new StubJwtProvider());

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.CreateInstallationTokenAsync(
            98765, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.DoesNotContain("app-jwt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHub_app_timeout_is_safe_dependency_failure()
    {
        var client = new GitHubAppClient(
            new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://api.github.com/") },
            new StubJwtProvider());

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetInstallationAsync(
            98765, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    private sealed class StubJwtProvider : IGitHubAppJwtProvider
    {
        public string CreateToken() => "app-jwt";
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
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
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
