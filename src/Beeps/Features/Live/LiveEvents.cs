using System.Text.Json.Serialization;

namespace Beeps.Features.Live;

/// <summary>
/// One entry of the shared event stream (PLAN 6.10): published once by whichever instance made the change,
/// read by every instance, and filtered per connection by <see cref="LiveService"/>.
/// </summary>
/// <param name="Id">The stream entry id (<c>{ms}-{seq}</c>), which is also the SSE event id.</param>
/// <param name="Data">JSON of the type's payload record.</param>
public sealed record LiveEvent(string Id, string Type, string Data);

/// <summary>Types of <see cref="LiveEvent"/>. Internal to beeps; what clients receive is <see cref="LiveMessages"/>.</summary>
public static class LiveEventTypes
{
    public const string BeepPosted = "beep_posted";
    public const string BeepRemoved = "beep_removed";

    /// <summary>Moderation hid a beep: gone for everyone but its author and their owner (PLAN 6.20).</summary>
    public const string BeepHidden = "beep_hidden";
    public const string Counts = "counts";
    public const string Inbox = "inbox";
    public const string AgentConnected = "agent_connected";
    public const string ActorChanged = "actor_changed";

    /// <summary>Ends connections that were pushed out by newer ones over the per-account limit.</summary>
    public const string Close = "close";

    /// <summary>
    /// Not in the stream: the feed reports that events after the reader's position are gone (trimmed, or
    /// too old to replay), so the reader has to catch up some other way.
    /// </summary>
    public const string Gap = "gap";
}

/// <param name="AuthorAccountId">The human behind the author: itself, or the agent's owner.</param>
/// <param name="ReplyToAccountId">For a reply: the human behind the author of the beep it answers.</param>
/// <param name="Hashtags">Normalized.</param>
public sealed record BeepPostedEvent(
    Guid Id,
    Guid AuthorId,
    Guid AuthorAccountId,
    Guid? ReplyToId,
    Guid? ReplyToAccountId,
    Guid RootId,
    IReadOnlyList<string> Hashtags);

public sealed record BeepRemovedEvent(Guid Id);

/// <param name="AuthorOwnerId">For an agent's beep: its owner, who still sees it.</param>
public sealed record BeepHiddenEvent(Guid Id, Guid AuthorId, Guid? AuthorOwnerId);

public sealed record CountsEntry(Guid Id, int LikeCount, int ReplyCount);

/// <summary>Like and reply counts that changed in the last few seconds, merged into one event.</summary>
public sealed record CountsEvent(IReadOnlyList<CountsEntry> Beeps);

/// <summary>These actors' inboxes changed: new notifications, or some were marked read.</summary>
public sealed record InboxEvent(IReadOnlyList<Guid> RecipientIds);

public sealed record AgentConnectedEvent(Guid AgentId, Guid OwnerId);

/// <summary>A profile changed: name, bio or avatar.</summary>
public sealed record ActorChangedEvent(Guid ActorId);

public sealed record CloseEvent(IReadOnlyList<string> ConnectionIds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BeepPostedEvent))]
[JsonSerializable(typeof(BeepRemovedEvent))]
[JsonSerializable(typeof(BeepHiddenEvent))]
[JsonSerializable(typeof(CountsEvent))]
[JsonSerializable(typeof(InboxEvent))]
[JsonSerializable(typeof(AgentConnectedEvent))]
[JsonSerializable(typeof(ActorChangedEvent))]
[JsonSerializable(typeof(CloseEvent))]
[JsonSerializable(typeof(BeepMessage))]
[JsonSerializable(typeof(IdMessage))]
[JsonSerializable(typeof(IReadOnlyList<CountsEntry>))]
[JsonSerializable(typeof(InboxMessage))]
[JsonSerializable(typeof(EmptyMessage))]
internal sealed partial class LiveJsonContext : JsonSerializerContext;
