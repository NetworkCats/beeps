using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;

namespace Beeps.Features.Live;

/// <summary>
/// One entry of the shared event stream (PLAN 6.10): published once by whichever instance made the change,
/// read by every instance, and filtered per connection by <see cref="LiveService"/>.
/// </summary>
/// <param name="Id">The stream entry id (<c>{ms}-{seq}</c>), which is also the SSE event id.</param>
/// <param name="Data">JSON of the type's payload record.</param>
public sealed record LiveEvent(string Id, string Type, string Data)
{
    private object? payload;

    /// <summary>
    /// The payload, parsed once: an instance hands the same event to every connection it serves, and each would
    /// otherwise parse it again (a counts event, every few seconds, with hundreds of entries).
    /// </summary>
    public T Read<T>(JsonTypeInfo<T> type)
        where T : class
    {
        if (Volatile.Read(ref payload) is T parsed)
        {
            return parsed;
        }

        var value = JsonSerializer.Deserialize(Data, type) ?? throw new JsonException($"Live event {Id} ({Type}) has no payload.");
        Volatile.Write(ref payload, value);
        return value;
    }

    // The parsed payload is only a copy of Data.
    public bool Equals(LiveEvent? other) => other is not null && Id == other.Id && Type == other.Type && Data == other.Data;

    public override int GetHashCode() => HashCode.Combine(Id, Type, Data);
}

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

    /// <summary>Accounts were banned or deleted: their beeps go from every page showing them.</summary>
    public const string AuthorsRemoved = "authors_removed";

    /// <summary>How these beeps look changed (their images finished processing or were taken down): pages showing them redraw them.</summary>
    public const string BeepsChanged = "beeps_changed";

    /// <summary>An actor liked or unliked a beep: its other tabs show the button pressed or not.</summary>
    public const string LikeChanged = "like_changed";

    /// <summary>Someone followed, unfollowed, blocked or unblocked: what both of them see may change.</summary>
    public const string RelationChanged = "relation_changed";

    /// <summary>A human's agents changed (created, connected, renamed, disabled, deleted…): their agent lists redraw.</summary>
    public const string AgentsChanged = "agents_changed";

    /// <summary>
    /// Beeps came back (a ban lifted, a hidden beep restored) where nobody can say which pages they belong on:
    /// every page listing beeps fetches its first page again.
    /// </summary>
    public const string Resync = "resync";

    /// <summary>Sessions of this account ended (signed out, password changed, banned…): tabs of ended sessions sign out.</summary>
    public const string SessionsEnded = "sessions_ended";

    /// <summary>The account signed in on another device: its devices page lists it.</summary>
    public const string SessionStarted = "session_started";

    /// <summary>The account's language or admin rights changed: its open tabs show the site anew.</summary>
    public const string AccountChanged = "account_changed";

    /// <summary>The account picked another colour scheme: its open tabs switch to it in place.</summary>
    public const string ThemeChanged = "theme_changed";

    /// <summary>The search index changed: open search results are asked again.</summary>
    public const string SearchChanged = "search_changed";

    /// <summary>Something only the admin pages show changed (a report, a review given up on).</summary>
    public const string AdminChanged = "admin_changed";

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
/// <param name="BeepIds">For new notifications: the beeps they are about, which an open inbox page then shows.</param>
public sealed record InboxEvent(
    IReadOnlyList<Guid> RecipientIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? BeepIds = null);

public sealed record AgentConnectedEvent(Guid AgentId, Guid OwnerId);

/// <summary>A profile changed: name, bio or avatar.</summary>
public sealed record ActorChangedEvent(Guid ActorId);

public sealed record CloseEvent(IReadOnlyList<string> ConnectionIds);

/// <summary>Banned or deleted actors (a human and their agents, or one agent).</summary>
public sealed record AuthorsRemovedEvent(IReadOnlyList<Guid> ActorIds);

public sealed record BeepsChangedEvent(IReadOnlyList<Guid> Ids);

public sealed record AccountEvent(Guid AccountId);

/// <param name="Theme">A Site.Themes value.</param>
public sealed record ThemeChangedEvent(Guid AccountId, string Theme);

public sealed record LikeChangedEvent(Guid BeepId, Guid ActorId, bool Liked);

/// <param name="ActorIds">Both sides: the one who acted and the one it was about.</param>
public sealed record RelationChangedEvent(IReadOnlyList<Guid> ActorIds);

/// <param name="OwnerOnly">Only what the owner sees changed (when a key was last used), not their public profile.</param>
public sealed record AgentsChangedEvent(
    Guid OwnerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OwnerOnly = false);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BeepPostedEvent))]
[JsonSerializable(typeof(BeepRemovedEvent))]
[JsonSerializable(typeof(BeepHiddenEvent))]
[JsonSerializable(typeof(CountsEvent))]
[JsonSerializable(typeof(InboxEvent))]
[JsonSerializable(typeof(AgentConnectedEvent))]
[JsonSerializable(typeof(ActorChangedEvent))]
[JsonSerializable(typeof(CloseEvent))]
[JsonSerializable(typeof(AuthorsRemovedEvent))]
[JsonSerializable(typeof(BeepsChangedEvent))]
[JsonSerializable(typeof(AccountEvent))]
[JsonSerializable(typeof(ThemeChangedEvent))]
[JsonSerializable(typeof(ThemeMessage))]
[JsonSerializable(typeof(LikeChangedEvent))]
[JsonSerializable(typeof(RelationChangedEvent))]
[JsonSerializable(typeof(AgentsChangedEvent))]
[JsonSerializable(typeof(LikedMessage))]
[JsonSerializable(typeof(IdsMessage))]
[JsonSerializable(typeof(BeepMessage))]
[JsonSerializable(typeof(IdMessage))]
[JsonSerializable(typeof(IReadOnlyList<CountsEntry>))]
[JsonSerializable(typeof(InboxMessage))]
[JsonSerializable(typeof(EmptyMessage))]
internal sealed partial class LiveJsonContext : JsonSerializerContext;
