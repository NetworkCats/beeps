using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Http;

/// <summary>The proxies whose <c>X-Forwarded-*</c> headers are believed (nginx only).</summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Single addresses, e.g. <c>127.0.0.1</c>.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>CIDR ranges, e.g. <c>172.18.0.0/16</c>.</summary>
    public List<string> KnownNetworks { get; set; } = [];
}

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddBeepsForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxyOptions>().BindConfiguration(TrustedProxyOptions.SectionName);
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxyOptions>>((options, trusted) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // nginx writes exactly one hop (the visitor IP it recovered from CF-Connecting-IP).
            options.ForwardLimit = 1;
            // The framework trusts loopback by default; only what is configured may be trusted.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in trusted.Value.KnownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in trusted.Value.KnownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            // With both lists empty the middleware skips the source check and believes anyone,
            // so "nothing configured" has to mean "read no forwarded headers at all".
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
            }
        });
        return services;
    }
}
