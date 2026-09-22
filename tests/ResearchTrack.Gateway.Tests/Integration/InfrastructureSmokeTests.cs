using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResearchTrack.Gateway;
using ResearchTrack.Testing;
using Yarp.ReverseProxy.Configuration;

namespace ResearchTrack.Gateway.Tests.Integration;

public sealed class InfrastructureSmokeTests : IAsyncLifetime
{
    private ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>? _factory;
    private HttpClient? _client;

    public ValueTask InitializeAsync()
    {
        _factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>();
        _client = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Liveness_endpoint_is_healthy()
    {
        var response = await Client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_document_is_available_in_testing_environment()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_uses_standard_error_envelope_with_trace_id()
    {
        var response = await Client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.False(payload!.Success);
        Assert.Equal("NOT_FOUND", payload.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(payload.Meta?.TraceId));
    }

    [Fact]
    public async Task Runtime_log_viewer_exposes_recent_logs_without_persistence()
    {
        await Client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        var viewer = await Client.GetAsync("/_runtime-logs", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        Assert.Equal("text/html", viewer.Content.Headers.ContentType?.MediaType);

        var entries = await Client.GetFromJsonAsync<JsonElement[]>(
            "/_runtime-logs/api/entries",
            TestContext.Current.CancellationToken);

        Assert.NotNull(entries);
        Assert.Contains(entries, entry =>
            entry.GetProperty("message").GetString()?.Contains("/health/live", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Runtime_log_viewer_does_not_log_its_own_requests()
    {
        await Client.PostAsync("/_runtime-logs/api/clear", null, TestContext.Current.CancellationToken);
        var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ViewerDiagnostics");
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestPath"] = "/_runtime-logs/assets/app.js"
        }))
        {
            logger.LogInformation("Framework diagnostic for runtime viewer request");
        }
        await Client.GetAsync("/_runtime-logs", TestContext.Current.CancellationToken);

        var entries = await Client.GetFromJsonAsync<JsonElement[]>(
            "/_runtime-logs/api/entries",
            TestContext.Current.CancellationToken);

        Assert.NotNull(entries);
        Assert.DoesNotContain(entries, entry =>
            entry.GetProperty("message").GetString()?.Contains("/_runtime-logs", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Supervisor_dashboard_route_targets_project_service()
    {
        var proxyConfigProvider = Factory.Services
            .GetRequiredService<IProxyConfigProvider>();
        var proxyConfig = proxyConfigProvider.GetConfig();
        var route = Assert.Single(
            proxyConfig.Routes,
            item => item.RouteId == "supervisor-dashboard-route");

        Assert.Equal("project", route.ClusterId);
        Assert.Equal("/api/v1/supervisor/dashboard", route.Match.Path);
    }

    [Fact]
    public void Frontend_github_contract_route_targets_github_service()
    {
        var proxyConfigProvider = Factory.Services
            .GetRequiredService<IProxyConfigProvider>();
        var proxyConfig = proxyConfigProvider.GetConfig();
        var route = Assert.Single(
            proxyConfig.Routes,
            item => item.RouteId == "github-frontend-contract-route");

        Assert.Equal("github", route.ClusterId);
        Assert.Equal("/api/github/{**catch-all}", route.Match.Path);
    }

    [Fact]
    public void Project_github_repositories_route_targets_github_service_before_project_route()
    {
        var proxyConfigProvider = Factory.Services
            .GetRequiredService<IProxyConfigProvider>();
        var proxyConfig = proxyConfigProvider.GetConfig();
        var route = Assert.Single(
            proxyConfig.Routes,
            item => item.RouteId == "project-github-repositories-route");

        Assert.Equal("github", route.ClusterId);
        Assert.Equal(-15, route.Order);
        Assert.Equal("/api/v1/projects/{projectId}/github-repositories", route.Match.Path);
    }

    [Fact]
    public void Frontend_project_github_repositories_contract_targets_github_service()
    {
        var proxyConfigProvider = Factory.Services
            .GetRequiredService<IProxyConfigProvider>();
        var proxyConfig = proxyConfigProvider.GetConfig();
        var route = Assert.Single(
            proxyConfig.Routes,
            item => item.RouteId == "project-github-repositories-frontend-contract-route");

        Assert.Equal("github", route.ClusterId);
        Assert.Equal(-15, route.Order);
        Assert.Equal("/api/projects/{projectId}/github-repositories", route.Match.Path);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");
    private ResearchTrackWebApplicationFactory<GatewayAssemblyMarker> Factory => _factory ?? throw new InvalidOperationException("Test factory is not initialized.");

    public sealed record ErrorEnvelope(bool Success, ErrorBody? Error, MetaBody? Meta);
    public sealed record ErrorBody(string Code, string Message);
    public sealed record MetaBody(string TraceId, DateTimeOffset Timestamp);
}
