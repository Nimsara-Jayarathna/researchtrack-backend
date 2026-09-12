using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using System.Net;
using System.Text;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Infrastructure.GitHubApp;

public sealed class GitHubInstallationApiClientTests
{
    [Fact]
    public async Task Uses_installation_token_only_in_authorization_header_for_github_api_call()
    {
        var handler = new StubHandler();
        var client = new GitHubInstallationApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

        using var payload = await client.GetAsync(
            "installation/repositories",
            new GitHubInstallationToken("installation-secret", DateTime.UtcNow.AddMinutes(30)),
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("installation-secret", handler.AuthorizationParameter);
        Assert.Equal("https://api.github.com/installation/repositories", handler.LastRequestUri?.AbsoluteUri);
        Assert.True(payload.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Repository_not_found_is_reported_as_not_found_not_dependency_success()
    {
        var client = new GitHubInstallationApiClient(
            new HttpClient(new StatusHandler(HttpStatusCode.NotFound))
            {
                BaseAddress = new Uri("https://api.github.com/")
            });

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync(
            "repositories/999",
            new GitHubInstallationToken("secret", DateTime.UtcNow.AddMinutes(30)),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/api")]
    [InlineData("//evil.example/api")]
    public async Task Rejects_absolute_or_scheme_relative_paths(string path)
    {
        var client = new GitHubInstallationApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("https://api.github.com/") });

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(
            path,
            new GitHubInstallationToken("secret", DateTime.UtcNow.AddMinutes(30)),
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Installation_api_unauthorized_or_forbidden_never_returns_repository_data(HttpStatusCode status)
    {
        var client = new GitHubInstallationApiClient(
            new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri("https://api.github.com/") });

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync(
            "installation/repositories",
            new GitHubInstallationToken("secret", DateTime.UtcNow.AddMinutes(30)),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task Installation_api_timeout_is_safe_dependency_failure()
    {
        var client = new GitHubInstallationApiClient(
            new HttpClient(new TimeoutHandler()) { BaseAddress = new Uri("https://api.github.com/") });

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync(
            "installation/repositories",
            new GitHubInstallationToken("secret", DateTime.UtcNow.AddMinutes(30)),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout"));
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
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
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        }
    }
}
