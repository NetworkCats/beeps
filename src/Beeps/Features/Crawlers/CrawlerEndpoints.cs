using System.Globalization;
using System.Text;
using System.Xml;
using Beeps.Features.Site;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Crawlers;

/// <summary>
/// <c>/robots.txt</c>, which keeps crawlers out of search results, and the sitemaps of what signed-out visitors can
/// read: <c>/sitemap.xml</c>, an index of <c>/sitemaps/pages.xml</c> and the profiles and beeps in chunks
/// (<see cref="ISitemapStore"/>). They name every profile, so they are for Google's and Bing's crawlers alone: any
/// other address is refused (<see cref="ICrawlerAddresses"/>).
/// </summary>
public static class CrawlerEndpoints
{
    public const string RobotsPath = "/robots.txt";

    public const string SitemapPath = "/sitemap.xml";

    /// <summary>The most a sitemap may list (sitemaps.org).</summary>
    internal const int ChunkSize = 50_000;

    private const string PagesPath = "/sitemaps/pages.xml";

    private const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>The site's own pages signed-out visitors can read.</summary>
    private static readonly string[] Pages = [SitePaths.Home, SitePaths.Public, SitePaths.About];

    public static IEndpointRouteBuilder MapCrawlerEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(RobotsPath, Robots).ExcludeFromDescription();
        var sitemaps = routes.MapGroup("").AddEndpointFilter(CrawlersOnlyAsync).ExcludeFromDescription();
        sitemaps.MapGet(SitemapPath, IndexAsync);
        sitemaps.MapGet(PagesPath, PagesAsync);
        sitemaps.MapGet("/sitemaps/profiles/{from:guid}.xml", ProfilesAsync);
        sitemaps.MapGet("/sitemaps/beeps/{from:guid}.xml", BeepsAsync);
        return routes;
    }

    private static ContentHttpResult Robots(IOptions<SiteOptions> site, HttpResponse response)
    {
        response.Headers.CacheControl = "public, max-age=3600";
        // Search results, as a page and as its data: endless addresses (one a query), and nothing a profile or beep
        // page does not already say.
        var text = $"""
            User-agent: *
            Disallow: /search
            Disallow: /page-data/search

            Sitemap: {site.Value.Absolute(SitemapPath).AbsoluteUri}

            """;
        return TypedResults.Text(text, "text/plain; charset=utf-8");
    }

    private static async ValueTask<object?> CrawlersOnlyAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        // Never kept on the way: the next caller may not be a crawler.
        context.Response.Headers.CacheControl = "no-store";
        var crawlers = context.RequestServices.GetRequiredService<ICrawlerAddresses>();
        if (!crawlers.IsLoaded)
        {
            // Not downloaded yet (a first start): a crawler comes back, rather than drop the sitemap.
            context.Response.Headers.RetryAfter = "3600";
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (context.Connection.RemoteIpAddress is not { } address || !crawlers.Contains(address))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(invocation);
    }

    private static async Task<EmptyHttpResult> IndexAsync(HttpContext context, ISitemapStore store, IOptions<SiteOptions> site, CancellationToken cancellationToken)
    {
        var profiles = await store.ListProfileChunksAsync(ChunkSize, cancellationToken);
        var beeps = await store.ListBeepChunksAsync(ChunkSize, cancellationToken);
        return await WriteAsync(context, "sitemapindex", async writer =>
        {
            await WriteEntryAsync(writer, "sitemap", site.Value.Absolute(PagesPath), null);
            foreach (var from in profiles)
            {
                await WriteEntryAsync(writer, "sitemap", site.Value.Absolute($"/sitemaps/profiles/{from:D}.xml"), null);
            }

            foreach (var from in beeps)
            {
                await WriteEntryAsync(writer, "sitemap", site.Value.Absolute($"/sitemaps/beeps/{from:D}.xml"), null);
            }
        });
    }

    private static Task<EmptyHttpResult> PagesAsync(HttpContext context, IOptions<SiteOptions> site) =>
        WriteAsync(context, "urlset", async writer =>
        {
            foreach (var page in Pages)
            {
                await WriteEntryAsync(writer, "url", site.Value.Absolute(page), null);
            }
        });

    private static async Task<EmptyHttpResult> ProfilesAsync(
        Guid from, HttpContext context, ISitemapStore store, IOptions<SiteOptions> site, CancellationToken cancellationToken)
    {
        var handles = await store.ListProfilesAsync(from, ChunkSize, cancellationToken);
        return await WriteAsync(context, "urlset", async writer =>
        {
            foreach (var handle in handles)
            {
                await WriteEntryAsync(writer, "url", site.Value.Absolute(SitePaths.Profile(handle)), null);
            }
        });
    }

    private static async Task<EmptyHttpResult> BeepsAsync(
        Guid from, HttpContext context, ISitemapStore store, IOptions<SiteOptions> site, CancellationToken cancellationToken)
    {
        var beeps = await store.ListBeepsAsync(from, ChunkSize, cancellationToken);
        return await WriteAsync(context, "urlset", async writer =>
        {
            foreach (var beep in beeps)
            {
                await WriteEntryAsync(writer, "url", site.Value.Absolute(SitePaths.Beep(beep.Id)), beep.CreatedAt);
            }
        });
    }

    /// <summary>A sitemap or the index, its entries written by <paramref name="entries"/> as they come.</summary>
    private static async Task<EmptyHttpResult> WriteAsync(HttpContext context, string root, Func<XmlWriter, Task> entries)
    {
        context.Response.ContentType = "application/xml; charset=utf-8";
        var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false) };
        await using var writer = XmlWriter.Create(context.Response.Body, settings);
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, root, Namespace);
        await entries(writer);
        await writer.WriteEndElementAsync();
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
        return TypedResults.Empty;
    }

    /// <summary>A <c>url</c> of a sitemap or a <c>sitemap</c> of the index: the same two elements in both.</summary>
    private static async Task WriteEntryAsync(XmlWriter writer, string element, Uri url, DateTimeOffset? modified)
    {
        await writer.WriteStartElementAsync(null, element, null);
        await writer.WriteElementStringAsync(null, "loc", null, url.AbsoluteUri);
        if (modified is { } at)
        {
            await writer.WriteElementStringAsync(null, "lastmod", null, at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }

        await writer.WriteEndElementAsync();
    }
}
