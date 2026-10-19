using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Live;

/// <param name="Watch">The page's <see cref="LiveTopic"/>, as text.</param>
/// <param name="Beeps">Comma-separated ids of the beeps on the page, for their counts and removal.</param>
/// <param name="LastEventId">The last event the browser received, when it reconnects.</param>
/// <param name="From">When the page was rendered (Unix milliseconds): on a first connection, what happened since is replayed.</param>
/// <param name="All">The page shows more beeps than it can list: everything about any beep is sent, and the page keeps what it shows.</param>
public sealed record LiveRequest(string? Watch, string? Beeps, string? LastEventId, long? From, bool All = false);

/// <summary>Who a browser connection is for, beyond the viewer.</summary>
/// <param name="SessionId">The tab's server-side session: when it ends elsewhere, the tab is told to sign out.</param>
/// <param name="Verified">False until the email address is verified: the tab then only follows its account (verified elsewhere, signed out).</param>
/// <param name="Admin">Only an admin's tab follows the admin pages: what changes there is not for anyone else to time.</param>
public sealed record LiveSession(Guid? SessionId, bool Verified, bool Admin = false);

public static class LiveErrorCodes
{
    public const string InvalidWatch = "live_watch_invalid";
    public const string InvalidBeeps = "live_beeps_invalid";
    public const string InvalidEventId = "live_event_id_invalid";
}

/// <summary>
/// Live connections (PLAN 6.10): one per browser tab for signed-in people, one per agent that wants its inbox.
/// Every connection reads the same stream of events and keeps what concerns its viewer and page; pages then
/// fetch new content rendered for them, so visibility is decided by the same queries as everywhere else.
/// </summary>
public sealed partial class LiveService(
    ILiveEventFeed feed,
    ILiveEventStore store,
    LiveUpdates updates,
    IProfileStore profiles,
    ISessionStore sessions,
    IAgentStore agents,
    NotificationService notifications,
    AdminWatchers adminWatchers,
    IOptions<LiveOptions> options,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<LiveService> logger)
{
    /// <summary>Beeps a page may list for counts and removal: a full thread page, with room to spare, while the URL stays far below proxies' 8 KB request line.</summary>
    public const int MaxWatchedBeeps = 120;

    /// <summary>A connection stops adding the new beeps it announces to what it watches beyond this.</summary>
    public const int MaxTrackedBeeps = 2000;

    /// <summary>Events that change what the admin pages show (counts, queues, people, beeps).</summary>
    private static readonly FrozenSet<string> AdminRelevant = FrozenSet.Create(
        StringComparer.Ordinal,
        LiveEventTypes.BeepPosted,
        LiveEventTypes.BeepRemoved,
        LiveEventTypes.BeepHidden,
        LiveEventTypes.BeepsChanged,
        LiveEventTypes.AuthorsRemoved,
        LiveEventTypes.AgentConnected,
        LiveEventTypes.AgentsChanged,
        LiveEventTypes.ActorChanged,
        LiveEventTypes.Resync,
        LiveEventTypes.AdminChanged);

    /// <summary>Clocks of the app and Valkey differ slightly; replaying a little more is harmless, since pages ignore what they already show.</summary>
    private static readonly TimeSpan ClockMargin = TimeSpan.FromSeconds(2);

    /// <summary>A signed-in person's tab: everything the page shows, live.</summary>
    public async Task<Result<IAsyncEnumerable<LiveMessage>>> OpenAsync(Viewer viewer, LiveSession session, LiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (!LiveTopic.TryParse(request.Watch, out var topic))
        {
            return Error.Validation(LiveErrorCodes.InvalidWatch, localizer["Error.LiveWatchInvalid"]);
        }

        if (!TryParseBeeps(request.Beeps, out var watched))
        {
            return Error.Validation(LiveErrorCodes.InvalidBeeps, localizer["Error.LiveBeepsInvalid", MaxWatchedBeeps]);
        }

        if (!TryResolveStart(request.LastEventId, request.From, out var after))
        {
            return Error.Validation(LiveErrorCodes.InvalidEventId, localizer["Error.LiveEventIdInvalid"]);
        }

        // As the home page read it (cached); read again, fresh, whenever the viewer follows or unfollows someone (a
        // relation_changed event), as the cached copy may not have been dropped yet when that event comes.
        var home = topic.Kind == LiveTopicKind.Home ? await HomeAccountsAsync(viewer, fresh: false, cancellationToken) : null;
        // A first connection is replayed a little from before the page was rendered; a reconnection from its last event.
        DateTimeOffset? renderedAt = request.LastEventId is null && request.From is { } from ? DateTimeOffset.FromUnixTimeMilliseconds(from) : null;
        var followed = session.Verified && (topic.Kind != LiveTopicKind.Admin || session.Admin) ? topic : LiveTopic.None;
        var connection = new Connection(NewConnectionId(), viewer, followed, watched, home)
        {
            SessionId = session.SessionId,
            RenderedAt = renderedAt,
            WatchesAll = request.All,
            AccountOnly = !session.Verified,
        };
        return Result.Of(StreamAsync(connection, after, resuming: request.LastEventId is not null, cancellationToken));
    }

    /// <summary>An agent's inbox (PLAN 4.3): the unread count on connecting and whenever it changes.</summary>
    /// <param name="keyId">The key the agent connected with: when it stops working, the stream ends.</param>
    public Result<IAsyncEnumerable<LiveMessage>> OpenInbox(Viewer agent, Guid? keyId, string? lastEventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return TryResolveStart(lastEventId, null, out var after)
            ? Result.Of(StreamAsync(new Connection(NewConnectionId(), agent, LiveTopic.None, [], null, inboxOnly: true) { KeyId = keyId }, after, resuming: false, cancellationToken))
            : Error.Validation(LiveErrorCodes.InvalidEventId, localizer["Error.LiveEventIdInvalid"]);
    }

    /// <param name="resuming">
    /// A page coming back from its last event (a reconnect, or moving to another page): it has the unread count already,
    /// and what changed it since comes again from the stream (or as a gap, which counts it), so it is not counted again
    /// on every page shown. Agents always get it on connecting, as skill.md says.
    /// </param>
    private async IAsyncEnumerable<LiveMessage> StreamAsync(Connection connection, LiveEventId? after, bool resuming, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RegisterAsync(connection, cancellationToken);
        // Figures with no events of their own are looked at only while an admin page follows them.
        using var watching = connection.Topic.Kind == LiveTopicKind.Admin ? adminWatchers.Watch() : null;
        try
        {
            using var subscription = feed.Subscribe(after);
            var first = resuming && !connection.InboxOnly
                ? Message(LiveMessages.Ping, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, after?.ToString())
                : await InboxAsync(connection, null, cancellationToken);
            yield return first with { Retry = options.Value.ReconnectDelay };

            var lastId = after?.ToString();
            await foreach (var item in WithHeartbeatAsync(subscription, cancellationToken))
            {
                if (item is null)
                {
                    yield return Message(LiveMessages.Ping, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, lastId);
                    continue;
                }

                if (item.Type != LiveEventTypes.Gap)
                {
                    lastId = item.Id;
                }

                foreach (var message in await HandleAsync(connection, item, cancellationToken))
                {
                    yield return message;
                    if (message.Type is LiveMessages.Close or LiveMessages.SignedOut)
                    {
                        yield break;
                    }
                }
            }
        }
        finally
        {
            await UnregisterAsync(connection);
        }
    }

    private async Task<IReadOnlyList<LiveMessage>> HandleAsync(Connection connection, LiveEvent item, CancellationToken cancellationToken)
    {
        try
        {
            if (item.Type == LiveEventTypes.RelationChanged && !connection.InboxOnly)
            {
                return await RelationChangedAsync(connection, item, cancellationToken);
            }

            if (connection.InboxOnly && MayEndAgentKey(connection, item))
            {
                return await AgentKeyChangedAsync(connection, item, cancellationToken);
            }

            if (item.Type == LiveEventTypes.SessionsEnded)
            {
                return await SessionsEndedAsync(connection, item, cancellationToken);
            }

            // Missed events: the page catches up, the unread count with it. A sign-out may be among them: the session is
            // checked again, as its own event would have. A tab not verified yet has no unread count; its page asks how
            // it is signed in after a reset, which shows a verification that was missed.
            if (item.Type == LiveEventTypes.Gap && !connection.InboxOnly)
            {
                if (connection.SessionId is { } sessionId && !await sessions.IsActiveAsync(sessionId, time.GetUtcNow(), cancellationToken))
                {
                    return [Message(LiveMessages.SignedOut, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage)];
                }

                var reset = Message(LiveMessages.Reset, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage);
                return connection.AccountOnly ? [reset] : [reset, await InboxAsync(connection, null, cancellationToken)];
            }

            // Not verified yet: nothing on the page is live but the account itself.
            if (connection.AccountOnly && item.Type is not (LiveEventTypes.AccountChanged or LiveEventTypes.ThemeChanged or LiveEventTypes.Close))
            {
                return [];
            }

            IReadOnlyList<LiveMessage> messages = Handle(connection, item) is { } message ? [message]
                : item.Type switch
                {
                    LiveEventTypes.Inbox when Read(item, LiveJsonContext.Default.InboxEvent) is var inbox && inbox.RecipientIds.Contains(connection.Viewer.ActorId) =>
                        [await InboxChangedAsync(connection, item.Id, inbox, cancellationToken)],
                    LiveEventTypes.Gap when connection.InboxOnly => [await InboxAsync(connection, null, cancellationToken)],
                    _ => [],
                };

            // The admin pages show figures and lists about everything: any change to the site may change them.
            return messages.Count == 0 && connection.Topic.Kind == LiveTopicKind.Admin && AdminRelevant.Contains(item.Type)
                ? [Message(LiveMessages.Admin, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id)]
                : messages;
        }
        catch (JsonException exception)
        {
            // Another version of the app may publish a shape this one does not know (rolling deploys).
            LogUnreadableEvent(logger, exception, item.Id, item.Type);
            return [];
        }
    }

    /// <summary>What an event means for this connection, when that needs no database; null when it means nothing or needs it.</summary>
    private static LiveMessage? Handle(Connection connection, LiveEvent item)
    {
        var viewer = connection.Viewer.ActorId;
        return item.Type switch
        {
            LiveEventTypes.Close when Read(item, LiveJsonContext.Default.CloseEvent).ConnectionIds.Contains(connection.Id, StringComparer.Ordinal) =>
                Message(LiveMessages.Close, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
            _ when connection.InboxOnly => null,
            // The account's own tabs, unverified ones too: it is how every page of theirs looks.
            LiveEventTypes.ThemeChanged when Read(item, LiveJsonContext.Default.ThemeChangedEvent) is var theme && theme.AccountId == connection.Viewer.AccountId =>
                Message(LiveMessages.Theme, new ThemeMessage(theme.Theme), LiveJsonContext.Default.ThemeMessage, item.Id),
            LiveEventTypes.BeepPosted => BeepPosted(connection, item),
            LiveEventTypes.BeepRemoved when Read(item, LiveJsonContext.Default.BeepRemovedEvent) is var removed && connection.Shows(removed.Id) =>
                Message(LiveMessages.Removed, new IdMessage(removed.Id), LiveJsonContext.Default.IdMessage, item.Id),
            // The author and their owner keep seeing it, now marked as under review; everyone else loses it.
            LiveEventTypes.BeepHidden when Read(item, LiveJsonContext.Default.BeepHiddenEvent) is var hidden && connection.Shows(hidden.Id) =>
                viewer == hidden.AuthorId || viewer == hidden.AuthorOwnerId
                    ? Message(LiveMessages.Changed, new IdsMessage([hidden.Id]), LiveJsonContext.Default.IdsMessage, item.Id)
                    : Message(LiveMessages.Removed, new IdMessage(hidden.Id), LiveJsonContext.Default.IdMessage, item.Id),
            LiveEventTypes.BeepsChanged => Changed(connection, item),
            LiveEventTypes.LikeChanged when Read(item, LiveJsonContext.Default.LikeChangedEvent) is var like
                && like.ActorId == viewer
                && connection.Shows(like.BeepId) =>
                Message(LiveMessages.Liked, new LikedMessage(like.BeepId, like.Liked), LiveJsonContext.Default.LikedMessage, item.Id),
            LiveEventTypes.AuthorsRemoved =>
                Message(LiveMessages.AuthorsRemoved, new IdsMessage(Read(item, LiveJsonContext.Default.AuthorsRemovedEvent).ActorIds), LiveJsonContext.Default.IdsMessage, item.Id),
            LiveEventTypes.Counts => Counts(connection, item),
            LiveEventTypes.AgentConnected when Read(item, LiveJsonContext.Default.AgentConnectedEvent) is var agent && agent.OwnerId == viewer =>
                Message(LiveMessages.AgentConnected, new IdMessage(agent.AgentId), LiveJsonContext.Default.IdMessage, item.Id),
            // Names and avatars show anywhere (beeps, mentions, lists of agents), so every page hears of them and checks.
            LiveEventTypes.ActorChanged =>
                Message(LiveMessages.Actor, new IdMessage(Read(item, LiveJsonContext.Default.ActorChangedEvent).ActorId), LiveJsonContext.Default.IdMessage, item.Id),
            LiveEventTypes.AgentsChanged when Read(item, LiveJsonContext.Default.AgentsChangedEvent) is var agents && (!agents.OwnerOnly || agents.OwnerId == viewer) =>
                Message(LiveMessages.Agents, new IdMessage(agents.OwnerId), LiveJsonContext.Default.IdMessage, item.Id),
            // Not when the page was rendered after the change: showing it anew would only replay the change, again and again.
            LiveEventTypes.AccountChanged when Read(item, LiveJsonContext.Default.AccountEvent).AccountId == connection.Viewer.AccountId && !RenderedSince(connection, item) =>
                Message(LiveMessages.Reload, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
            LiveEventTypes.SearchChanged when connection.Topic.Kind == LiveTopicKind.Search =>
                Message(LiveMessages.Search, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
            // Every page: lists add what came back, other pages (older pages of a list, say) draw their list again.
            LiveEventTypes.Resync => Message(LiveMessages.Resync, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
            _ => null,
        };
    }

    /// <summary>
    /// The viewer followed, unfollowed, blocked or unblocked someone, or was blocked: a home timeline follows the new
    /// set of people, the page catches up with what it lists, and blocks change what the inbox counts.
    /// </summary>
    private async Task<IReadOnlyList<LiveMessage>> RelationChangedAsync(Connection connection, LiveEvent item, CancellationToken cancellationToken)
    {
        if (!Read(item, LiveJsonContext.Default.RelationChangedEvent).ActorIds.Contains(connection.Viewer.ActorId))
        {
            return [];
        }

        if (connection.Home is not null)
        {
            connection.Home = await HomeAccountsAsync(connection.Viewer, fresh: true, cancellationToken);
        }

        return
        [
            Message(LiveMessages.Resync, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
            await InboxAsync(connection, item.Id, cancellationToken),
        ];
    }

    /// <summary>Sessions of the viewer's account ended: if this tab's is one of them, it signs out and the connection ends.</summary>
    private async Task<IReadOnlyList<LiveMessage>> SessionsEndedAsync(Connection connection, LiveEvent item, CancellationToken cancellationToken)
    {
        if (connection.SessionId is not { } sessionId
            || Read(item, LiveJsonContext.Default.AccountEvent).AccountId != connection.Viewer.AccountId
            || await sessions.IsActiveAsync(sessionId, time.GetUtcNow(), cancellationToken))
        {
            return [];
        }

        return [Message(LiveMessages.SignedOut, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id)];
    }

    /// <summary>
    /// Whether the event may mean the agent's key stopped working: its owner's agents changed (key regenerated or revoked,
    /// agent disabled or deleted), the agent or its owner was removed, or the owner's sessions ended (a ban).
    /// </summary>
    private static bool MayEndAgentKey(Connection connection, LiveEvent item)
    {
        var owner = connection.Viewer.OwnerId;
        return item.Type switch
        {
            LiveEventTypes.AgentsChanged => Read(item, LiveJsonContext.Default.AgentsChangedEvent) is { OwnerOnly: false } changed && changed.OwnerId == owner,
            LiveEventTypes.AuthorsRemoved => Read(item, LiveJsonContext.Default.AuthorsRemovedEvent).ActorIds.Any(id => id == connection.Viewer.ActorId || id == owner),
            LiveEventTypes.SessionsEnded => Read(item, LiveJsonContext.Default.AccountEvent).AccountId == owner,
            _ => false,
        };
    }

    /// <summary>An agent whose key no longer works is told the stream is closed, as when it opened too many: it must not reconnect with it.</summary>
    private async Task<IReadOnlyList<LiveMessage>> AgentKeyChangedAsync(Connection connection, LiveEvent item, CancellationToken cancellationToken) =>
        connection.KeyId is { } keyId && !await agents.IsKeyActiveAsync(keyId, cancellationToken)
            ? [Message(LiveMessages.Close, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id)]
            : [];

    private async Task<IReadOnlySet<Guid>> HomeAccountsAsync(Viewer viewer, bool fresh, CancellationToken cancellationToken) =>
        new HashSet<Guid>(fresh
            ? await profiles.ListFollowedAsync(viewer.AccountId, cancellationToken)
            : await profiles.ListFollowedCachedAsync(viewer.AccountId, cancellationToken)) { viewer.AccountId };

    private static bool RenderedSince(Connection connection, LiveEvent item) =>
        connection.RenderedAt is { } rendered && LiveEventId.TryParse(item.Id, out var id) && id.Time < rendered;

    private static LiveMessage? Changed(Connection connection, LiveEvent item)
    {
        List<Guid> shown = [.. Read(item, LiveJsonContext.Default.BeepsChangedEvent).Ids.Where(connection.Shows)];
        return shown.Count == 0 ? null : Message(LiveMessages.Changed, new IdsMessage(shown), LiveJsonContext.Default.IdsMessage, item.Id);
    }

    private static LiveMessage? BeepPosted(Connection connection, LiveEvent item)
    {
        var beep = Read(item, LiveJsonContext.Default.BeepPostedEvent);
        if (!connection.Topic.Matches(beep, connection.Home))
        {
            return null;
        }

        // The page is about to show it, so its counts and removal concern this connection too.
        if (connection.Watched.Count < MaxTrackedBeeps)
        {
            connection.Watched.Add(beep.Id);
        }

        return Message(LiveMessages.Beep, new BeepMessage(beep.Id, beep.RootId, beep.ReplyToId), LiveJsonContext.Default.BeepMessage, item.Id);
    }

    private static LiveMessage? Counts(Connection connection, LiveEvent item)
    {
        List<CountsEntry> shown = [.. Read(item, LiveJsonContext.Default.CountsEvent).Beeps.Where(b => connection.Shows(b.Id))];
        return shown.Count == 0 ? null : Message(LiveMessages.Counts, (IReadOnlyList<CountsEntry>)shown, LiveJsonContext.Default.IReadOnlyListCountsEntry, item.Id);
    }

    /// <summary>The inbox page is about to show the beeps of new notifications, so their counts and removal concern it too.</summary>
    private Task<LiveMessage> InboxChangedAsync(Connection connection, string eventId, InboxEvent inbox, CancellationToken cancellationToken)
    {
        if (connection.Topic.Kind == LiveTopicKind.Notifications)
        {
            foreach (var beepId in inbox.BeepIds ?? [])
            {
                if (connection.Watched.Count >= MaxTrackedBeeps)
                {
                    break;
                }

                connection.Watched.Add(beepId);
            }
        }

        return InboxAsync(connection, eventId, cancellationToken);
    }

    private async Task<LiveMessage> InboxAsync(Connection connection, string? eventId, CancellationToken cancellationToken)
    {
        var unread = await notifications.CountUnreadAsync(connection.Viewer, cancellationToken);
        return Message(LiveMessages.Inbox, new InboxMessage(unread), LiveJsonContext.Default.InboxMessage, eventId);
    }

    /// <summary>Merges the events with a ping whenever none came for the heartbeat interval (PLAN 6.10); null is a ping.</summary>
    private async IAsyncEnumerable<LiveEvent?> WithHeartbeatAsync(ILiveSubscription subscription, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = subscription.ReadAllAsync(reading.Token).GetAsyncEnumerator(reading.Token);
        var next = events.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                using (var beat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var delay = Task.Delay(options.Value.HeartbeatInterval, time, beat.Token);
                    if (await Task.WhenAny(next, delay) == delay)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return null;
                        continue;
                    }

                    await beat.CancelAsync();
                }

                if (!await next)
                {
                    yield break;
                }

                yield return events.Current;
                next = events.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // An enumerator must not be disposed while a read is pending: cancel it and let it finish first.
            await reading.CancelAsync();
            try
            {
                await next;
            }
            catch (OperationCanceledException)
            {
            }

            await events.DisposeAsync();
        }
    }

    private async Task RegisterAsync(Connection connection, CancellationToken cancellationToken)
    {
        try
        {
            var evicted = await store.AddConnectionAsync(
                connection.Viewer.ActorId, connection.Id, time.GetUtcNow(), options.Value.MaxConnectionsPerActor, cancellationToken);
            await updates.CloseConnectionsAsync(evicted, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Without Valkey the limit cannot be counted; the connection still works for what reaches this instance.
            LogConnectionBookkeepingFailed(logger, exception, connection.Viewer.ActorId);
        }
    }

    private async Task UnregisterAsync(Connection connection)
    {
        try
        {
            // The request is over by now, so its token is cancelled already.
            await store.RemoveConnectionAsync(connection.Viewer.ActorId, connection.Id, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogConnectionBookkeepingFailed(logger, exception, connection.Viewer.ActorId);
        }
    }

    /// <summary>A reconnecting browser names the last event it got; a fresh page names when it was rendered.</summary>
    private bool TryResolveStart(string? lastEventId, long? from, out LiveEventId? after)
    {
        after = null;
        if (!string.IsNullOrEmpty(lastEventId))
        {
            if (!LiveEventId.TryParse(lastEventId, out var last))
            {
                return false;
            }

            after = last;
            return true;
        }

        if (from is { } milliseconds)
        {
            if (milliseconds < 0)
            {
                return false;
            }

            var rendered = DateTimeOffset.FromUnixTimeMilliseconds(Math.Min(milliseconds, time.GetUtcNow().ToUnixTimeMilliseconds()));
            after = LiveEventId.Before(rendered - ClockMargin);
        }

        return true;
    }

    private static bool TryParseBeeps(string? value, out HashSet<Guid> beeps)
    {
        beeps = [];
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var id) || (beeps.Add(id) && beeps.Count > MaxWatchedBeeps))
            {
                return false;
            }
        }

        return true;
    }

    private string NewConnectionId() => Guid.CreateVersion7(time.GetUtcNow()).ToString("N");

    private static T Read<T>(LiveEvent item, JsonTypeInfo<T> type)
        where T : class => item.Read(type);

    private static LiveMessage Message<T>(string type, T payload, JsonTypeInfo<T> payloadType, string? id = null) =>
        new(type, JsonSerializer.Serialize(payload, payloadType), id);

    /// <param name="watched">Beeps whose counts and removal the page shows; grows with the new beeps the connection announces.</param>
    /// <param name="home">For a home timeline: the viewer and the humans they follow.</param>
    /// <param name="inboxOnly">An agent's connection, which only follows its inbox.</param>
    private sealed class Connection(string id, Viewer viewer, LiveTopic topic, HashSet<Guid> watched, IReadOnlySet<Guid>? home, bool inboxOnly = false)
    {
        public string Id { get; } = id;

        public Viewer Viewer { get; } = viewer;

        public LiveTopic Topic { get; } = topic;

        public HashSet<Guid> Watched { get; } = watched;

        /// <summary>For a home timeline: the viewer and the humans they follow, read again when that changes.</summary>
        public IReadOnlySet<Guid>? Home { get; set; } = home;

        public bool InboxOnly { get; } = inboxOnly;

        /// <summary>An agent's stream: the key it was opened with.</summary>
        public Guid? KeyId { get; init; }

        /// <summary>A person's tab: the session it was opened in.</summary>
        public Guid? SessionId { get; init; }

        /// <summary>On a page's first connection: when the page was rendered.</summary>
        public DateTimeOffset? RenderedAt { get; init; }

        /// <summary>The page shows more beeps than it could list: everything about any beep concerns it.</summary>
        public bool WatchesAll { get; init; }

        /// <summary>A tab of an account whose email is not verified yet: only the account's own changes concern it.</summary>
        public bool AccountOnly { get; init; }

        public bool Shows(Guid beepId) => WatchesAll || Watched.Contains(beepId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped live event {EventId} ({EventType}): its data could not be read")]
    private static partial void LogUnreadableEvent(ILogger logger, Exception exception, string eventId, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the live connections of {ActorId} failed")]
    private static partial void LogConnectionBookkeepingFailed(ILogger logger, Exception exception, Guid actorId);
}
