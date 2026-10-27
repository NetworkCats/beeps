using System.Globalization;
using Beeps.Features.Crawlers;
using Beeps.Features.Site;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Shared.Http;

public sealed class PublicPageCacheOptions
{
    public const string SectionName = "PublicPageCache";

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds, not minutes: signed-out visitors get no live updates, so they see at most this much staleness.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Memory an instance gives its copies, in megabytes (counted as their bodies' size). The framework's default is
    /// 100: a crawl through thousands of pages filled it in a minute, and the copies, long expired, stayed until the next.
    /// </summary>
    public int SizeLimitMegabytes { get; set; } = 32;
}

/// <summary>
/// Output caching for public pages read by signed-out visitors: timelines, threads,
/// hashtags and profiles. Signed-in pages carry personal state (likes, follow buttons) and are never
/// cached; the framework's default policy already skips any authenticated request, and any response that
/// sets a cookie, so a page with an antiforgery token is never stored either. Pages carry <see cref="PageTags"/>,
/// so a removed beep or a changed profile is evicted from them at once on every instance.
/// </summary>
public static class PublicPageCache
{
    /// <summary>Pages.Islands.IslandRequests.Header, which marks a request for a page's data: Features do not depend on the pages.</summary>
    private const string IslandsHeader = "X-Beeps-Islands";

    /// <summary>Put on a PageModel as <c>[OutputCache(PolicyName = PublicPageCache.Policy)]</c>.</summary>
    public const string Policy = "PublicPage";

    /// <summary>
    /// The only query keys the cached pages read (their cursors). Anything else (<c>?fbclid=</c>, <c>?utm_source=</c>,
    /// a random <c>?x=1</c>) shares the page's copy instead of rendering it anew for every click.
    /// </summary>
    private static readonly string[] QueryKeys = ["before", "after"];

    /// <summary>The largest page kept; a timeline or profile page is about 70 KB.</summary>
    private const int MaximumBodyBytes = 1024 * 1024;

    /// <summary>
    /// The query string a cached page may echo (an address that comes back to it): only <see cref="QueryKeys"/>, as the
    /// copy is shared by every query that differs in nothing else.
    /// </summary>
    public static QueryString CachedQuery(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kept = QueryKeys.Where(request.Query.ContainsKey).SelectMany(key => request.Query[key].Select(value => KeyValuePair.Create(key, value)));
        return QueryString.Create(kept);
    }

    public static IServiceCollection AddBeepsOutputCache(this IServiceCollection services)
    {
        services.AddOptions<PublicPageCacheOptions>().BindConfiguration(PublicPageCacheOptions.SectionName);
        services.AddOutputCache();
        services.AddOptions<OutputCacheOptions>().Configure<IOptions<PublicPageCacheOptions>>((options, page) =>
        {
            options.SizeLimit = Math.Max(1, page.Value.SizeLimitMegabytes) * 1024L * 1024L;
            // A page larger than this (a very long thread) is drawn each time rather than taking a share of the cache.
            options.MaximumBodySize = MaximumBodyBytes;
            options.AddPolicy(Policy, builder =>
            {
                builder.AddPolicy<RevalidatePolicy>();
                if (!page.Value.Enabled)
                {
                    builder.NoCache();
                    return;
                }

                // Same URL, different language or colour scheme: both come from cookies (or Accept-Language), not the URL.
                builder.Expire(page.Value.Duration)
                    .Tag(PageTags.All)
                    .VaryByValue(_ => new KeyValuePair<string, string>("culture", CultureInfo.CurrentUICulture.Name))
                    .VaryByValue(context => new KeyValuePair<string, string>("theme", ThemeCookie.Read(context.Request) ?? Themes.System))
                    // Visitors read beeps translated, crawlers as written (Translation.VisitorLanguages).
                    .VaryByValue(context => new KeyValuePair<string, string>("crawler", context.FromCrawler() ? "yes" : "no"))
                    // A page's data (/page-data/…) is asked for as the page itself, marked by this header (Pages/Islands).
                    .SetVaryByHeader(IslandsHeader)
                    .SetVaryByQuery(QueryKeys);
            });
        });
        return services;
    }

    /// <summary>
    /// The copy is the server's alone. The same address is drawn for each language, colour scheme and person, by
    /// cookie, so no cache on the way (a CDN told to cache pages) or browser may keep one. Set before the page runs:
    /// stored with the copy, and replaced by what a signed-in answer says (no-store).
    /// </summary>
    private sealed class RevalidatePolicy : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-cache";
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;
    }
}
