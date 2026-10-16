using System.Globalization;
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
/// sets a cookie, so a page with an antiforgery token is never stored either.
/// </summary>
public static class PublicPageCache
{
    /// <summary>Put on a PageModel as <c>[OutputCache(PolicyName = PublicPageCache.Policy)]</c>.</summary>
    public const string Policy = "PublicPage";

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

                // Same URL, different language: the culture comes from a cookie or Accept-Language, not the URL.
                builder.Expire(page.Value.Duration)
                    .VaryByValue(_ => new KeyValuePair<string, string>("culture", CultureInfo.CurrentUICulture.Name));
            }));
        return services;
    }
}
