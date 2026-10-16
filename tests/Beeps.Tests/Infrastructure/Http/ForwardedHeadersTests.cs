using System.Net;
using Beeps.Infrastructure.Http;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Beeps.Tests.Infrastructure.Http;

public sealed class ForwardedHeadersTests
{
    [Fact]
    public async Task NothingIsTrustedByDefaultNotEvenLoopback()
    {
        var seen = await RequestAsync(peer: "127.0.0.1", forwardedFor: "203.0.113.9", trusted: []);

        Assert.Equal("127.0.0.1|http", seen);
    }

    [Fact]
    public async Task ConfiguredProxyIsBelieved()
    {
        var seen = await RequestAsync(peer: "127.0.0.1", forwardedFor: "203.0.113.9", trusted: [("KnownProxies:0", "127.0.0.1")]);

        Assert.Equal("203.0.113.9|https", seen);
    }

    [Fact]
    public async Task ConfiguredNetworkIsBelieved()
    {
        var seen = await RequestAsync(peer: "172.18.0.1", forwardedFor: "203.0.113.9", trusted: [("KnownNetworks:0", "172.18.0.0/16")]);

        Assert.Equal("203.0.113.9|https", seen);
    }

    [Fact]
    public async Task DirectClientsCannotSpoofTheirAddress()
    {
        var seen = await RequestAsync(peer: "198.51.100.4", forwardedFor: "203.0.113.9", trusted: [("KnownProxies:0", "127.0.0.1")]);

        Assert.Equal("198.51.100.4|http", seen);
    }

    [Fact]
    public async Task OnlyTheHopNginxAddedIsUsed()
    {
        // A visitor can send their own X-Forwarded-For; nginx appends the real address last.
        var seen = await RequestAsync(peer: "127.0.0.1", forwardedFor: "10.0.0.1, 203.0.113.9", trusted: [("KnownProxies:0", "127.0.0.1")]);

        Assert.Equal("203.0.113.9|https", seen);
    }

    [Fact]
    public async Task InvalidAddressesFailAtStartup()
    {
        await Assert.ThrowsAnyAsync<FormatException>(
            () => RequestAsync(peer: "127.0.0.1", forwardedFor: "203.0.113.9", trusted: [("KnownProxies:0", "not-an-ip")]));
    }

    private static async Task<string> RequestAsync(string peer, string forwardedFor, (string Key, string Value)[] trusted)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    trusted.Select(t => KeyValuePair.Create<string, string?>($"{TrustedProxyOptions.SectionName}:{t.Key}", t.Value))))
                .ConfigureServices(services => services.AddBeepsForwardedHeaders())
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                        return next(context);
                    });
                    app.UseForwardedHeaders();
                    app.Run(context => context.Response.WriteAsync($"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}"));
                }))
            .StartAsync(TestSupport.Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await host.GetTestClient().SendAsync(request, TestSupport.Ct);
        return await response.Content.ReadAsStringAsync(TestSupport.Ct);
    }
}
