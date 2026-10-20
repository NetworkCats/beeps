using System.Diagnostics.CodeAnalysis;
using Beeps.Features.Beeps;

namespace Beeps.Features.Live;

public enum LiveTopicKind
{
    /// <summary>Nothing to add to the page; counts, removals and the inbox still update.</summary>
    None,
    Home,
    Public,

    /// <summary>A profile's top-level beeps (the "beeps" tab).</summary>
    Profile,

    /// <summary>A profile's replies (the "replies" tab).</summary>
    ProfileReplies,
    Tag,
    Thread,
    Notifications,

    /// <summary>Search results: drawn again when the search index changes.</summary>
    Search,

    /// <summary>The admin pages: drawn again when anything happens on the site.</summary>
    Admin,
}

/// <summary>
/// What new content a page shows as it arrives (PLAN 6.10: each tab subscribes to its page). Pages give it to
/// their page island (<c>LiveProps.Watch</c>); the browser passes it back as <c>watch</c> on the stream and when it
/// fetches new beeps (<c>/live/beeps</c>). The text form is <c>home</c>, <c>public</c>, <c>profile:{id}</c>, <c>replies:{id}</c>,
/// <c>tag:{normalized tag}</c>, <c>thread:{root id}</c>, <c>notifications</c>, <c>search</c>, <c>admin</c>, or empty.
/// </summary>
public sealed record LiveTopic(LiveTopicKind Kind, Guid Id = default, string Tag = "")
{
    public static readonly LiveTopic None = new(LiveTopicKind.None);
    public static readonly LiveTopic Home = new(LiveTopicKind.Home);
    public static readonly LiveTopic Public = new(LiveTopicKind.Public);
    public static readonly LiveTopic Notifications = new(LiveTopicKind.Notifications);
    public static readonly LiveTopic Search = new(LiveTopicKind.Search);
    public static readonly LiveTopic Admin = new(LiveTopicKind.Admin);

    /// <summary>Lists that grow at the top, newest first; a thread grows at the bottom.</summary>
    public bool IsBeepList => Kind is LiveTopicKind.Home or LiveTopicKind.Public or LiveTopicKind.Profile or LiveTopicKind.ProfileReplies or LiveTopicKind.Tag;

    public static LiveTopic Profile(Guid actorId, bool replies) => new(replies ? LiveTopicKind.ProfileReplies : LiveTopicKind.Profile, actorId);

    public static LiveTopic ForTag(string normalizedTag) => new(LiveTopicKind.Tag, Tag: normalizedTag);

    public static LiveTopic Thread(Guid rootId) => new(LiveTopicKind.Thread, rootId);

    public static bool TryParse(string? value, [NotNullWhen(true)] out LiveTopic? topic)
    {
        topic = null;
        if (string.IsNullOrEmpty(value))
        {
            topic = None;
            return true;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? value : value[..colon];
        var argument = colon < 0 ? null : value[(colon + 1)..];
        topic = (name, argument) switch
        {
            ("home", null) => Home,
            ("public", null) => Public,
            ("notifications", null) => Notifications,
            ("search", null) => Search,
            ("admin", null) => Admin,
            ("profile", { } id) when TryParseId(id, out var actorId) => Profile(actorId, replies: false),
            ("replies", { } id) when TryParseId(id, out var actorId) => Profile(actorId, replies: true),
            ("thread", { } id) when TryParseId(id, out var rootId) => Thread(rootId),
            ("tag", { } tag) when BeepText.TryNormalizeHashtag(tag, out var normalized) => ForTag(normalized),
            _ => null,
        };
        return topic is not null;
    }

    /// <summary>
    /// Whether a new beep belongs on a page watching this topic. Coarse on purpose: the page fetches what is
    /// new with the same query that rendered it, which applies visibility and every other rule exactly.
    /// </summary>
    /// <param name="homeAccounts">For <see cref="LiveTopicKind.Home"/>: the viewer and the humans they follow.</param>
    public bool Matches(BeepPostedEvent beep, IReadOnlySet<Guid>? homeAccounts)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return Kind switch
        {
            LiveTopicKind.Home => homeAccounts is not null
                && homeAccounts.Contains(beep.AuthorAccountId)
                && (beep.ReplyToAccountId is not { } parent || homeAccounts.Contains(parent)),
            LiveTopicKind.Public => beep.ReplyToId is null,
            LiveTopicKind.Profile => beep.ReplyToId is null && (beep.AuthorId == Id || beep.AuthorAccountId == Id),
            LiveTopicKind.ProfileReplies => beep.ReplyToId is not null && (beep.AuthorId == Id || beep.AuthorAccountId == Id),
            LiveTopicKind.Tag => beep.Hashtags.Contains(Tag, StringComparer.Ordinal),
            LiveTopicKind.Thread => beep.RootId == Id,
            _ => false,
        };
    }

    public override string ToString() => Kind switch
    {
        LiveTopicKind.Home => "home",
        LiveTopicKind.Public => "public",
        LiveTopicKind.Notifications => "notifications",
        LiveTopicKind.Search => "search",
        LiveTopicKind.Admin => "admin",
        LiveTopicKind.Profile => $"profile:{Id:D}",
        LiveTopicKind.ProfileReplies => $"replies:{Id:D}",
        LiveTopicKind.Thread => $"thread:{Id:D}",
        LiveTopicKind.Tag => $"tag:{Tag}",
        _ => "",
    };

    private static bool TryParseId(string value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;
}
