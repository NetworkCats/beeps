using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
public sealed record LiveRequest(string? Watch, string? Beeps, string? LastEventId, long? From);

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
    NotificationService notifications,
    IOptions<LiveOptions> options,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<LiveService> logger)
{
    /// <summary>Beeps a page may list for counts and removal: a full thread page, with room to spare, while the URL stays far below proxies' 8 KB request line.</summary>
    public const int MaxWatchedBeeps = 120;

    /// <summary>A connection stops adding the new beeps it announces to what it watches beyond this.</summary>
    public const int MaxTrackedBeeps = 2000;

    /// <summary>Clocks of the app and Valkey differ slightly; replaying a little more is harmless, since pages ignore what they already show.</summary>
    private static readonly TimeSpan ClockMargin = TimeSpan.FromSeconds(2);

    /// <summary>A signed-in person's tab: everything the page shows, live.</summary>
    public async Task<Result<IAsyncEnumerable<LiveMessage>>> OpenAsync(Viewer viewer, LiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
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

        // Read once: following someone happens on another page, which opens a new connection.
        IReadOnlySet<Guid>? home = topic.Kind == LiveTopicKind.Home
            ? new HashSet<Guid>(await profiles.ListFollowedAsync(viewer.AccountId, cancellationToken)) { viewer.AccountId }
            : null;
        return Result.Of(StreamAsync(new Connection(NewConnectionId(), viewer, topic, watched, home), after, cancellationToken));
    }

    /// <summary>An agent's inbox (PLAN 4.3): the unread count on connecting and whenever it changes.</summary>
    public Result<IAsyncEnumerable<LiveMessage>> OpenInbox(Viewer agent, string? lastEventId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return TryResolveStart(lastEventId, null, out var after)
            ? Result.Of(StreamAsync(new Connection(NewConnectionId(), agent, LiveTopic.None, [], null, InboxOnly: true), after, cancellationToken))
            : Error.Validation(LiveErrorCodes.InvalidEventId, localizer["Error.LiveEventIdInvalid"]);
    }

    private async IAsyncEnumerable<LiveMessage> StreamAsync(Connection connection, LiveEventId? after, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RegisterAsync(connection, cancellationToken);
        try
        {
            using var subscription = feed.Subscribe(after);
            var inbox = await InboxAsync(connection, null, cancellationToken);
            yield return inbox with { Retry = options.Value.ReconnectDelay };

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

                var message = await HandleAsync(connection, item, cancellationToken);
                if (message is null)
                {
                    continue;
                }

                yield return message;
                if (message.Type == LiveMessages.Close)
                {
                    yield break;
                }
            }
        }
        finally
        {
            await UnregisterAsync(connection);
        }
    }

    private async Task<LiveMessage?> HandleAsync(Connection connection, LiveEvent item, CancellationToken cancellationToken)
    {
        try
        {
            return item.Type switch
            {
                LiveEventTypes.Inbox when Read(item, LiveJsonContext.Default.InboxEvent).RecipientIds.Contains(connection.Viewer.ActorId) =>
                    await InboxAsync(connection, item.Id, cancellationToken),
                LiveEventTypes.Close when Read(item, LiveJsonContext.Default.CloseEvent).ConnectionIds.Contains(connection.Id, StringComparer.Ordinal) =>
                    Message(LiveMessages.Close, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, item.Id),
                LiveEventTypes.Gap => connection.InboxOnly
                    ? await InboxAsync(connection, null, cancellationToken)
                    : Message(LiveMessages.Reset, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage),
                _ when connection.InboxOnly => null,
                LiveEventTypes.BeepPosted => BeepPosted(connection, item),
                LiveEventTypes.BeepRemoved when Read(item, LiveJsonContext.Default.BeepRemovedEvent) is var removed && connection.Watched.Contains(removed.Id) =>
                    Message(LiveMessages.Removed, new IdMessage(removed.Id), LiveJsonContext.Default.IdMessage, item.Id),
                LiveEventTypes.Counts => Counts(connection, item),
                LiveEventTypes.AgentConnected when Read(item, LiveJsonContext.Default.AgentConnectedEvent) is var agent && agent.OwnerId == connection.Viewer.ActorId =>
                    Message(LiveMessages.AgentConnected, new IdMessage(agent.AgentId), LiveJsonContext.Default.IdMessage, item.Id),
                LiveEventTypes.ActorChanged when Read(item, LiveJsonContext.Default.ActorChangedEvent) is var actor
                    && connection.Topic.Kind is LiveTopicKind.Profile or LiveTopicKind.ProfileReplies
                    && connection.Topic.Id == actor.ActorId =>
                    Message(LiveMessages.Actor, new IdMessage(actor.ActorId), LiveJsonContext.Default.IdMessage, item.Id),
                _ => null,
            };
        }
        catch (JsonException exception)
        {
            // Another version of the app may publish a shape this one does not know (rolling deploys).
            LogUnreadableEvent(logger, exception, item.Id, item.Type);
            return null;
        }
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
        List<CountsEntry> shown = [.. Read(item, LiveJsonContext.Default.CountsEvent).Beeps.Where(b => connection.Watched.Contains(b.Id))];
        return shown.Count == 0 ? null : Message(LiveMessages.Counts, (IReadOnlyList<CountsEntry>)shown, LiveJsonContext.Default.IReadOnlyListCountsEntry, item.Id);
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
                connection.Viewer.ActorId, connection.Id, time.GetUtcNow(), options.Value.MaxConnectionsPerAccount, cancellationToken);
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

    private static T Read<T>(LiveEvent item, JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(item.Data, type) ?? throw new JsonException($"Live event {item.Id} ({item.Type}) has no payload.");

    private static LiveMessage Message<T>(string type, T payload, JsonTypeInfo<T> payloadType, string? id = null) =>
        new(type, JsonSerializer.Serialize(payload, payloadType), id);

    /// <param name="Watched">Beeps whose counts and removal the page shows; grows with the new beeps the connection announces.</param>
    /// <param name="Home">For a home timeline: the viewer and the humans they follow.</param>
    /// <param name="InboxOnly">An agent's connection, which only follows its inbox.</param>
    private sealed record Connection(string Id, Viewer Viewer, LiveTopic Topic, HashSet<Guid> Watched, IReadOnlySet<Guid>? Home, bool InboxOnly = false);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped live event {EventId} ({EventType}): its data could not be read")]
    private static partial void LogUnreadableEvent(ILogger logger, Exception exception, string eventId, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the live connections of {AccountId} failed")]
    private static partial void LogConnectionBookkeepingFailed(ILogger logger, Exception exception, Guid accountId);
}
