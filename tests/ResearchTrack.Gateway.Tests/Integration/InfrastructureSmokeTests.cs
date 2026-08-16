using System.Net;
using System.Net.Http.Json;
using ResearchTrack.Testing;

namespace ResearchTrack.Gateway.Tests.Integration;

public sealed class InfrastructureSmokeTests : IAsyncLifetime
{
    private ResearchTrackWebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public ValueTask InitializeAsync()
    {
        _factory = new ResearchTrackWebApplicationFactory<Program>();
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

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient Client => _client ?? throw new InvalidOperationException("Test client is not initialized.");

    public sealed record ErrorEnvelope(bool Success, ErrorBody? Error, MetaBody? Meta);
    public sealed record ErrorBody(string Code, string Message);
    public sealed record MetaBody(string TraceId, DateTimeOffset Timestamp);
}
