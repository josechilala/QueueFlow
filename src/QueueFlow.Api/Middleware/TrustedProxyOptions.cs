using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace QueueFlow.Api.Middleware;

public static class TrustedProxyOptions
{
    public static ForwardedHeadersOptions Create(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = configuration.GetValue("ReverseProxy:ForwardLimit", 1),
        };
        if (options.ForwardLimit is < 1 or > 32) throw new InvalidOperationException("ReverseProxy:ForwardLimit must be between 1 and 32.");
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var value in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            options.KnownProxies.Add(IPAddress.Parse(value));
        foreach (var value in configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
        {
            var network = System.Net.IPNetwork.Parse(value);
            if (network.PrefixLength == 0) throw new InvalidOperationException("Trusting all proxy addresses is forbidden.");
            options.KnownIPNetworks.Add(network);
        }
        // Empty lists disable ASP.NET's trust checks. A non-routable sentinel keeps
        // forwarding disabled until the operator explicitly configures a proxy.
        if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            options.KnownProxies.Add(IPAddress.None);
        return options;
    }
}
