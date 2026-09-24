using Microsoft.AspNetCore.HttpOverrides;

namespace ResearchTrack.Gateway;

/// <summary>
/// Resolves the real client IP and scheme when the Gateway runs behind reverse proxies
/// (Nginx Proxy Manager on the Test VPS; Nginx plus the Container Apps ingress in Azure
/// Production). Without this, per-IP rate limiting sees every user as the proxy address.
/// </summary>
public static class TrustedProxyForwarding
{
    // The Gateway is never directly internet-reachable: in every environment its callers
    // are proxies on private or carrier-grade NAT (Container Apps ingress) addresses.
    // Only hops from these ranges are trusted to append X-Forwarded-* values.
    private static readonly string[] TrustedNetworks =
    [
        "127.0.0.0/8",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "100.64.0.0/10",
        "::1/128",
        "fc00::/7"
    ];

    public static IServiceCollection AddTrustedProxyForwarding(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(Configure);
        return services;
    }

    public static void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Walk X-Forwarded-For from the right through every trusted hop and stop at the
        // first untrusted address, which is the client. Values a client prepends itself
        // sit left of that address and are ignored.
        options.ForwardLimit = null;

        // Proxies append X-Forwarded-For but may only set X-Forwarded-Proto once.
        options.RequireHeaderSymmetry = false;

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in TrustedNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
}
