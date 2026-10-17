using Beeps.Features.Beeps;
using Microsoft.AspNetCore.OutputCaching;

namespace Beeps.Features.Shared.Http;

/// <summary>
/// Tags on output-cached public pages (<see cref="PublicPageCache"/>), so that a change evicts exactly the cached pages
/// it affects, on every instance, instead of waiting for them to expire. Pages carry two kinds: where new beeps belong
/// (<see cref="Public"/>, <see cref="Thread"/>, <see cref="Actor"/>, <see cref="Hashtag"/>), and what they show
/// (<see cref="Beep"/>, <see cref="Shows"/>), for changes to something already on them.
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

    /// <summary>A page that shows this beep: its counts, its images, whether it is there at all.</summary>
    public static string Beep(Guid beepId) => $"pages:beep:{beepId:D}";

    /// <summary>A page that shows this actor anywhere: as an author, a mention, a profile, an owner or an agent.</summary>
    public static string Shows(Guid actorId) => $"pages:shows:{actorId:D}";

    /// <summary>What a page showing these beeps depends on: each beep, its author and the actors it mentions.</summary>
    public static IEnumerable<string> Showing(IEnumerable<BeepView> beeps) =>
        beeps.SelectMany(b => b.Mentions.Select(m => Shows(m.Id)).Append(Shows(b.Author.Id)).Append(Beep(b.Id)));

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
