using System.Net;
using System.Text;
using ResearchTrack.JiraService.Configuration;
using ResearchTrack.JiraService.Infrastructure;

namespace ResearchTrack.JiraService.Tests.Infrastructure;

public sealed class AtlassianClientTokenTests
{
    [Fact]
    public async Task RefreshTokenAsync_PersistsRotatedRefreshTokenFromAtlassianResponse()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"access_token":"new-access","refresh_token":"rotated-refresh","expires_in":3600,"scope":"read:jira-work"}""");
        var client = Create(handler);

        var result = await client.RefreshTokenAsync("old-refresh", TestContext.Current.CancellationToken);

        Assert.Equal("new-access", result.AccessToken);
        Assert.Equal("rotated-refresh", result.RefreshToken);
        Assert.Equal("read:jira-work", result.Scope);
        Assert.NotNull(result.ExpiresAt);
        Assert.Contains("old-refresh", handler.LastBody ?? string.Empty);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenAtlassianOmitsRotatedRefreshToken_KeepsExistingRefreshToken()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"access_token":"new-access","expires_in":1200}""");
        var client = Create(handler);

        var result = await client.RefreshTokenAsync("existing-refresh", TestContext.Current.CancellationToken);

        Assert.Equal("existing-refresh", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenAtlassianRejectsRefresh_ThrowsReconnectMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "{} ");
        var client = Create(handler);

        var error = await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(
            () => client.RefreshTokenAsync("expired-refresh", TestContext.Current.CancellationToken));

        Assert.Contains("Reconnect Jira", error.Message);
    }

    private static AtlassianClient Create(HttpMessageHandler handler)
    {
        var options = new JiraOptions
        {
            TokenUrl = "https://auth.atlassian.test/token",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://researchtrack.test/callback"
        };
        return new AtlassianClient(new HttpClient(handler), options);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
