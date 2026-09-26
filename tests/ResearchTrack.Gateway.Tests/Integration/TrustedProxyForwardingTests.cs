using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ResearchTrack.Gateway.Tests.Integration;

public sealed class TrustedProxyForwardingTests
{
    [Fact]
    public async Task Client_ip_is_resolved_through_nginx_and_container_apps_ingress()
    {
        // Internet client -> Nginx (10.20.10.4) -> Container Apps ingress -> Gateway.
        var context = await RunAsync(
            remoteIp: "100.100.0.7",
            forwardedFor: "203.0.113.9, 10.20.10.4",
            forwardedProto: "https");

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task Client_supplied_forwarded_for_values_are_ignored()
    {
        // The client tried to spoof 198.51.100.77; Nginx appended the real address.
        var context = await RunAsync(
            remoteIp: "172.18.0.5",
            forwardedFor: "198.51.100.77, 203.0.113.9",
            forwardedProto: "https");

        Assert.Equal(IPAddress.Parse("203.0.113.9"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Forwarded_headers_from_untrusted_peers_are_not_applied()
    {
        var context = await RunAsync(
            remoteIp: "198.51.100.20",
            forwardedFor: "203.0.113.9",
            forwardedProto: "https");

        Assert.Equal(IPAddress.Parse("198.51.100.20"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    private static async Task<HttpContext> RunAsync(string remoteIp, string forwardedFor, string forwardedProto)
    {
        var options = new ForwardedHeadersOptions();
        TrustedProxyForwarding.Configure(options);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));
        await middleware.Invoke(context);
        return context;
    }
}
