using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Features.Shared.Http;

/// <summary>
/// Tags on output-cached public pages (<see cref="PublicPageCache"/>), so that removing a beep or changing a
/// profile evicts exactly the cached pages that show it, on every instance, instead of waiting for them to expire.
/// </summary>
public static class PageTags
{
    /// <summary>Every cached page, for changes that may show anywhere (a banned or deleted account's beeps).</summary>
    public const string All = "pages";

    /// <summary>The public timeline, which the signed-out home page also shows.</summary>
    public const string Public = "pages:public";

    public static string Thread(Guid rootId) => $"pages:thread:{rootId:D}";

    /// <summary>The actor's profile tabs.</summary>
    public static string Actor(Guid actorId) => $"pages:actor:{actorId:D}";

    public static string Hashtag(string normalizedTag) => $"pages:tag:{normalizedTag}";

    /// <summary>Tags the page being rendered, if it is going to be cached.</summary>
    public static void Add(HttpContext context, params IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Features.Get<IOutputCacheFeature>()?.Context is { } cache)
        {
            cache.Tags.UnionWith(tags);
        }
    }
}
