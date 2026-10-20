using System.Globalization;
using Beeps.Features.Site;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Shared.Http;

public sealed class PublicPageCacheOptions
{
    public const string SectionName = "PublicPageCache";

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds, not minutes (PLAN 6.6): signed-out visitors get no live updates, so they see at most this much staleness.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Output caching for public pages read by signed-out visitors (PLAN 6.6, 6.10): timelines, threads,
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
            options.AddPolicy(Policy, builder =>
            {
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
                    // A page's data (/page-data/…) is asked for as the page itself, marked by this header (Pages/Islands).
                    .SetVaryByHeader(IslandsHeader)
                    .SetVaryByQuery(QueryKeys);
            }));
        return services;
    }
}
