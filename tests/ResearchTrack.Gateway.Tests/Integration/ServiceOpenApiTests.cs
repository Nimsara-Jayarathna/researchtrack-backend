using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ResearchTrack.Gateway;
using ResearchTrack.Testing;
using Yarp.ReverseProxy.Forwarder;

namespace ResearchTrack.Gateway.Tests.Integration;

public sealed class ServiceOpenApiTests
{
    [Theory]
    [InlineData("auth", 5101)]
    [InlineData("project", 5102)]
    [InlineData("github", 5103)]
    [InlineData("jira", 5104)]
    [InlineData("meeting", 5105)]
    [InlineData("submission", 5106)]
    public async Task Service_document_is_forwarded_to_the_correct_cluster_with_rewritten_path(string service, int port)
    {
        var forwarder = new DocumentClientFactory();
        await using var factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>(
            configureServices: services => services.Replace(
                ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(forwarder)));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/swagger/{service}/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"openapi\":\"3.0.4\"}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal($"http://localhost:{port}/swagger/v1/swagger.json", forwarder.RequestUri?.ToString());
    }

    [Fact]
    public async Task Ui_lists_all_services_and_retains_the_gateway_document()
    {
        await using var factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>();
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Contains("index.js", html);
        var script = await client.GetStringAsync("/swagger/index.js", TestContext.Current.CancellationToken);
        foreach (var service in ServiceOpenApi.Services)
        {
            Assert.Contains($"{service}/v1/swagger.json", script);
        }
        Assert.Contains("ResearchTrack Gateway v1", script);
        using var response = await client.GetAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/swagger/unknown/v1/swagger.json")]
    [InlineData("/swagger/auth/health/ready")]
    [InlineData("/swagger/auth/v2/swagger.json")]
    public async Task Swagger_routes_do_not_expose_arbitrary_downstream_paths(string path)
    {
        await using var factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Production", false, 0)]
    [InlineData("Production", true, 6)]
    [InlineData("Development", false, 6)]
    public void Proxy_document_routes_follow_the_same_enablement_as_swagger(string environment, bool enabled, int count)
    {
        var configuration = new ConfigurationManager();
        configuration["OpenApi:Enabled"] = enabled.ToString();
        ServiceOpenApi.ConfigureRoutes(configuration, new TestEnvironment { EnvironmentName = environment });
        Assert.Equal(count, configuration.GetSection("ReverseProxy:Routes").GetChildren().Count());
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/auth/v1/swagger.json")]
    public async Task Disabled_openapi_hides_ui_and_all_document_routes(string path)
    {
        await using var baseFactory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenApi:Enabled"] = "false"
                })));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Downstream_failure_is_preserved(HttpStatusCode status)
    {
        var forwarder = new DocumentClientFactory { Status = status };
        await using var factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>(
            configureServices: services => services.Replace(
                ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(forwarder)));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/auth/v1/swagger.json", TestContext.Current.CancellationToken);
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task Document_route_does_not_forward_post_requests()
    {
        var forwarder = new DocumentClientFactory();
        await using var factory = new ResearchTrackWebApplicationFactory<GatewayAssemblyMarker>(
            configureServices: services => services.Replace(
                ServiceDescriptor.Singleton<IForwarderHttpClientFactory>(forwarder)));
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/swagger/auth/v1/swagger.json", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Null(forwarder.RequestUri);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DocumentClientFactory : IForwarderHttpClientFactory
    {
        public Uri? RequestUri { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
            new(new DocumentHandler(uri => RequestUri = uri, Status));
    }

    private sealed class DocumentHandler(Action<Uri?> capture, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"openapi\":\"3.0.4\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}
