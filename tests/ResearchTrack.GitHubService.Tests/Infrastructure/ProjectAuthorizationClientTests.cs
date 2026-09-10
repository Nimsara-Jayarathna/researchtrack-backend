using System.Net;
using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Infrastructure;

public sealed class ProjectAuthorizationClientTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Forwards_bearer_authentication_to_project_service()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var client = CreateClient(handler, accessor);

        await client.EnsureCanManageAsync(ProjectId, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer access-token", handler.Authorization);
        Assert.Equal($"http://project.test/api/v1/projects/{ProjectId}", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Inaccessible_project_is_reported_as_forbidden()
    {
        var client = CreateClient(
            new StubHandler(HttpStatusCode.NotFound),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.EnsureCanManageAsync(ProjectId, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    private static ProjectAuthorizationClient CreateClient(
        HttpMessageHandler handler,
        IHttpContextAccessor accessor) => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://project.test/") },
            accessor);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}
