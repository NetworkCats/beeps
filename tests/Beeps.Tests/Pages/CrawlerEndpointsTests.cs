using System.Globalization;
using System.Net;
using System.Xml.Linq;
using Beeps.Features.Crawlers;
using Beeps.Infrastructure.Crawlers;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

public sealed class CrawlerEndpointsTests : IDisposable
{
    private static readonly XNamespace Sitemaps = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly BeepsWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task RobotsKeepCrawlersOutOfSearchResultsAndPointAtTheSitemap()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/robots.txt", UriKind.Relative), TestSupport.Ct);
        var text = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("User-agent: *\nDisallow: /search\nDisallow: /page-data/search\n", text, StringComparison.Ordinal);
        Assert.Contains("Sitemap: https://beeps.test/sitemap.xml\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACrawlerGetsTheIndexOfTheSitemaps()
    {
        Crawlers().Use([IPNetwork.Parse("66.249.64.0/27"), IPNetwork.Parse("2001:4860:4801:10::/64")]);
        var profiles = new[] { Guid.CreateVersion7(TestData.Now), Guid.CreateVersion7(TestData.Now.AddDays(1)) };
        var beeps = new[] { Guid.CreateVersion7(TestData.Now) };
        factory.Sitemap.ListProfileChunksAsync(CrawlerEndpoints.ChunkSize, Arg.Any<CancellationToken>()).Returns(profiles);
        factory.Sitemap.ListBeepChunksAsync(CrawlerEndpoints.ChunkSize, Arg.Any<CancellationToken>()).Returns(beeps);

        var (status, headers, body) = await GetAsync("/sitemap.xml", "::ffff:66.249.64.7");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal("application/xml; charset=utf-8", headers.ContentType);
        Assert.Equal("no-store", headers.CacheControl);
        var root = XDocument.Parse(body).Root!;
        Assert.Equal(Sitemaps + "sitemapindex", root.Name);
        Assert.Equal(
            [
                "https://beeps.test/sitemaps/pages.xml",
                $"https://beeps.test/sitemaps/profiles/{profiles[0]:D}.xml",
                $"https://beeps.test/sitemaps/profiles/{profiles[1]:D}.xml",
                $"https://beeps.test/sitemaps/beeps/{beeps[0]:D}.xml",
            ],
            Locations(root, "sitemap"));

        (status, _, _) = await GetAsync("/sitemap.xml", "2001:4860:4801:10::1");
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task EachSitemapTheIndexNamesListsItsPages()
    {
        Crawlers().Use([IPNetwork.Parse("66.249.64.0/27")]);
        var from = Guid.CreateVersion7(TestData.Now);
        var beep = Guid.CreateVersion7(TestData.Now);
        factory.Sitemap.ListProfilesAsync(from, CrawlerEndpoints.ChunkSize, Arg.Any<CancellationToken>()).Returns(["tom", "tom.claude"]);
        factory.Sitemap.ListBeepsAsync(from, CrawlerEndpoints.ChunkSize, Arg.Any<CancellationToken>()).Returns([new SitemapBeep(beep, TestData.Now)]);

        var pages = XDocument.Parse((await GetAsync("/sitemaps/pages.xml", "66.249.64.7")).Body).Root!;
        var profiles = XDocument.Parse((await GetAsync($"/sitemaps/profiles/{from:D}.xml", "66.249.64.7")).Body).Root!;
        var beeps = XDocument.Parse((await GetAsync($"/sitemaps/beeps/{from:D}.xml", "66.249.64.7")).Body).Root!;

        Assert.Equal(["https://beeps.test/", "https://beeps.test/public", "https://beeps.test/about"], Locations(pages, "url"));
        Assert.Equal(["https://beeps.test/@tom", "https://beeps.test/@tom.claude"], Locations(profiles, "url"));
        Assert.Equal([$"https://beeps.test/beeps/{beep:D}"], Locations(beeps, "url"));
        Assert.Equal(
            TestData.Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            beeps.Element(Sitemaps + "url")!.Element(Sitemaps + "lastmod")!.Value);
        Assert.Null(profiles.Element(Sitemaps + "url")!.Element(Sitemaps + "lastmod"));
    }

    [Theory]
    [InlineData("/sitemap.xml")]
    [InlineData("/sitemaps/pages.xml")]
    [InlineData("/sitemaps/profiles/0190f5c2-0000-7000-8000-000000000000.xml")]
    [InlineData("/sitemaps/beeps/0190f5c2-0000-7000-8000-000000000000.xml")]
    public async Task AnyoneElseIsRefused(string path)
    {
        Crawlers().Use([IPNetwork.Parse("66.249.64.0/27")]);

        var (status, headers, _) = await GetAsync(path, "203.0.113.9");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        // The error page adds no-cache of its own.
        Assert.Contains("no-store", headers.CacheControl.ToString(), StringComparison.Ordinal);
        Assert.Empty(factory.Sitemap.ReceivedCalls());
    }

    [Fact]
    public async Task UntilTheAddressesAreLoadedCrawlersAreAskedToComeBack()
    {
        var (status, headers, _) = await GetAsync("/sitemap.xml", "66.249.64.7");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("3600", headers.RetryAfter);
        Assert.Empty(factory.Sitemap.ReceivedCalls());
    }

    private static IEnumerable<string> Locations(XElement root, string entry) =>
        root.Elements(Sitemaps + entry).Select(e => e.Element(Sitemaps + "loc")!.Value);

    private CrawlerAddressList Crawlers() => factory.Services.GetRequiredService<CrawlerAddressList>();

    /// <summary>From <paramref name="address"/>, as the forwarded headers leave it: the test client has none of its own.</summary>
    private async Task<(int Status, IHeaderDictionary Headers, string Body)> GetAsync(string path, string address)
    {
        var context = await factory.Server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = path;
            c.Connection.RemoteIpAddress = IPAddress.Parse(address);
        }, TestSupport.Ct);
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, context.Response.Headers, await reader.ReadToEndAsync(TestSupport.Ct));
    }
}
