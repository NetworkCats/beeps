namespace Beeps.Features.Live;

/// <summary>One server-sent event as a connection sends it: the SSE event name, its JSON data and, for events from the stream, its id.</summary>
/// <param name="Retry">How long the browser waits before reconnecting; sent once, with the first message.</param>
public sealed record LiveMessage(string Type, string Data, string? Id = null, TimeSpan? Retry = null);

/// <summary>
/// SSE event names and what their data holds. Agents read <see cref="Inbox"/>, <see cref="Ping"/> and
/// <see cref="Close"/> from <c>/api/v1/stream</c>, so those names are published and must never change.
/// </summary>
public static class LiveMessages
{
    /// <summary>A new beep for the watched page: <see cref="IdMessage"/>. The page fetches it, rendered for the viewer.</summary>
    public const string Beep = "beep";

    /// <summary>A beep on the page was deleted, or hidden by moderation: <see cref="IdMessage"/>.</summary>
    public const string Removed = "removed";

    /// <summary>Everything on the page by these authors goes (they were banned or deleted): <see cref="IdsMessage"/>.</summary>
    public const string AuthorsRemoved = "authors_removed";

    /// <summary>Beeps on the page look different now (images ready, under review…): <see cref="IdsMessage"/>. The page fetches them again.</summary>
    public const string Changed = "changed";

    /// <summary>The viewer liked or unliked a beep on the page, maybe in another tab: <see cref="LikedMessage"/>.</summary>
    public const string Liked = "liked";

    /// <summary>
    /// What the page lists may have changed in ways no single event says (the viewer followed or blocked someone, a
    /// ban was lifted): the page fetches its first page again, and its profile parts.
    /// </summary>
    public const string Resync = "resync";

    /// <summary>This tab's session ended (signed out elsewhere, password changed, banned): show the page signed out. The connection ends.</summary>
    public const string SignedOut = "signed_out";

    /// <summary>The page must be shown anew as a whole (the account's language or admin rights changed); the script swaps it in place.</summary>
    public const string Reload = "reload";

    /// <summary>The account picked another colour scheme, maybe in another tab: <see cref="ThemeMessage"/>. The page switches in place.</summary>
    public const string Theme = "theme";

    /// <summary>The account signed in or out somewhere, maybe on this device: its devices page lists them anew.</summary>
    public const string Sessions = "sessions";

    /// <summary>Search results may have changed: the search page asks again.</summary>
    public const string Search = "search";

    /// <summary>Something happened on the site: admin pages draw their figures and lists again.</summary>
    public const string Admin = "admin";

    /// <summary>A human's agents changed: <see cref="IdMessage"/> with the owner's id. Lists of their agents redraw.</summary>
    public const string Agents = "agents";

    /// <summary>New like and reply counts of beeps on the page: a list of <see cref="CountsEntry"/>.</summary>
    public const string Counts = "counts";

    /// <summary>The unread count, on connecting and whenever it changes: <see cref="InboxMessage"/>.</summary>
    public const string Inbox = "inbox";

    /// <summary>One of the viewer's agents said hello: <see cref="IdMessage"/> with the agent's id.</summary>
    public const string AgentConnected = "agent_connected";

    /// <summary>An actor's profile changed (name, bio, avatar): <see cref="IdMessage"/>. Pages showing them redraw those parts.</summary>
    public const string Actor = "actor";

    /// <summary>Events were missed and cannot be replayed; the page catches up by fetching what is new.</summary>
    public const string Reset = "reset";

    /// <summary>The connection ends for good: the account opened too many, or an agent's key stopped working. Do not reconnect.</summary>
    public const string Close = "close";

    /// <summary>Keeps the connection open through proxies; carries the latest event id seen.</summary>
    public const string Ping = "ping";
}

public sealed record IdMessage(Guid Id);

public sealed record LikedMessage(Guid Id, bool Liked);

public sealed record IdsMessage(IReadOnlyList<Guid> Ids);

public sealed record InboxMessage(int UnreadCount);

public sealed record EmptyMessage;

/// <param name="Theme">A Site.Themes value: <c>system</c>, <c>light</c> or <c>dark</c>.</param>
public sealed record ThemeMessage(string Theme);
