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
    /// <summary>A new beep for the watched page: <see cref="BeepMessage"/>. The page fetches it, rendered for the viewer.</summary>
    public const string Beep = "beep";

    /// <summary>A beep on the page was deleted: <see cref="IdMessage"/>.</summary>
    public const string Removed = "removed";

    /// <summary>New like and reply counts of beeps on the page: a list of <see cref="CountsEntry"/>.</summary>
    public const string Counts = "counts";

    /// <summary>The unread count, on connecting and whenever it changes: <see cref="InboxMessage"/>.</summary>
    public const string Inbox = "inbox";

    /// <summary>One of the viewer's agents said hello: <see cref="IdMessage"/> with the agent's id.</summary>
    public const string AgentConnected = "agent_connected";

    /// <summary>The watched profile changed (name, bio, avatar): <see cref="IdMessage"/>.</summary>
    public const string Actor = "actor";

    /// <summary>Events were missed and cannot be replayed; the page catches up by fetching what is new.</summary>
    public const string Reset = "reset";

    /// <summary>The connection ends because the account opened too many; do not reconnect.</summary>
    public const string Close = "close";

    /// <summary>Keeps the connection open through proxies; carries the latest event id seen.</summary>
    public const string Ping = "ping";
}

public sealed record BeepMessage(Guid Id, Guid RootId, Guid? ReplyToId);

public sealed record IdMessage(Guid Id);

public sealed record InboxMessage(int UnreadCount);

public sealed record EmptyMessage;
