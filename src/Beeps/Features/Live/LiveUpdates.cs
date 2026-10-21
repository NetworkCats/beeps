using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;

namespace Beeps.Features.Live;

/// <summary>
/// Publishes live events (PLAN 12: every change that alters a page publishes one). Called after the change is
/// committed; live updates are best effort, so a Valkey outage is logged and never fails the change itself.
/// </summary>
public sealed partial class LiveUpdates(ILiveEventStore store, IBeepCounterStore counters, ILogger<LiveUpdates> logger)
{
    /// <summary>Beeps per merged counts event.</summary>
    public const int CountsBatchSize = 500;

    private readonly LogThrottle publishFailures = new();

    public Task BeepPostedAsync(BeepPostedEvent beep, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.BeepPosted, beep, LiveJsonContext.Default.BeepPostedEvent, cancellationToken);

    public Task BeepRemovedAsync(Guid beepId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.BeepRemoved, new BeepRemovedEvent(beepId), LiveJsonContext.Default.BeepRemovedEvent, cancellationToken);

    public Task BeepHiddenAsync(Guid beepId, Guid authorId, Guid? authorOwnerId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.BeepHidden, new BeepHiddenEvent(beepId, authorId, authorOwnerId), LiveJsonContext.Default.BeepHiddenEvent, cancellationToken);

    public Task InboxChangedAsync(IReadOnlyList<Guid> recipientIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipientIds);
        return recipientIds.Count == 0
            ? Task.CompletedTask
            : AppendAsync(LiveEventTypes.Inbox, new InboxEvent(recipientIds), LiveJsonContext.Default.InboxEvent, cancellationToken);
    }

    /// <summary>New notifications about this beep: an open inbox page shows it, so its counts and removal concern that page.</summary>
    public Task NotifiedAsync(IReadOnlyList<Guid> recipientIds, Guid beepId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipientIds);
        return recipientIds.Count == 0
            ? Task.CompletedTask
            : AppendAsync(LiveEventTypes.Inbox, new InboxEvent(recipientIds, [beepId]), LiveJsonContext.Default.InboxEvent, cancellationToken);
    }

    public Task AgentConnectedAsync(Guid agentId, Guid ownerId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.AgentConnected, new AgentConnectedEvent(agentId, ownerId), LiveJsonContext.Default.AgentConnectedEvent, cancellationToken);

    /// <summary>After a ban or a deletion: open pages drop these actors' beeps without a reload.</summary>
    public Task AuthorsRemovedAsync(IReadOnlyList<Guid> actorIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorIds);
        return actorIds.Count == 0
            ? Task.CompletedTask
            : AppendAsync(LiveEventTypes.AuthorsRemoved, new AuthorsRemovedEvent(actorIds), LiveJsonContext.Default.AuthorsRemovedEvent, cancellationToken);
    }

    /// <summary>The beeps look different (images ready or removed): pages showing them redraw them.</summary>
    public Task BeepsChangedAsync(IReadOnlyList<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        return beepIds.Count == 0
            ? Task.CompletedTask
            : AppendAsync(LiveEventTypes.BeepsChanged, new BeepsChangedEvent(beepIds), LiveJsonContext.Default.BeepsChangedEvent, cancellationToken);
    }

    /// <summary>So the actor's other tabs show the like button as it now is.</summary>
    public Task LikeChangedAsync(Guid beepId, Guid actorId, bool liked, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.LikeChanged, new LikeChangedEvent(beepId, actorId, liked), LiveJsonContext.Default.LikeChangedEvent, cancellationToken);

    /// <summary>A follow or block between the two changed: their home timelines, profiles and inboxes may show something else now.</summary>
    public Task RelationChangedAsync(Guid actorId, Guid otherId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.RelationChanged, new RelationChangedEvent([actorId, otherId]), LiveJsonContext.Default.RelationChangedEvent, cancellationToken);

    /// <summary>The owner's agents changed: lists of them (the agents page, the owner's profile) redraw.</summary>
    public Task AgentsChangedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.AgentsChanged, new AgentsChangedEvent(ownerId), LiveJsonContext.Default.AgentsChangedEvent, cancellationToken);

    /// <summary>An agent's key was used (recorded every few minutes at most): the owner's agents page shows when.</summary>
    public Task AgentKeyUsedAsync(Guid ownerId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.AgentsChanged, new AgentsChangedEvent(ownerId, OwnerOnly: true), LiveJsonContext.Default.AgentsChangedEvent, cancellationToken);

    /// <summary>The account signed in somewhere: its devices page lists the new device.</summary>
    public Task SessionStartedAsync(Guid accountId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.SessionStarted, new AccountEvent(accountId), LiveJsonContext.Default.AccountEvent, cancellationToken);

    /// <summary>Sessions of the account were ended: its tabs check whether theirs is among them, and its devices page drops them.</summary>
    public Task SessionsEndedAsync(Guid accountId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.SessionsEnded, new AccountEvent(accountId), LiveJsonContext.Default.AccountEvent, cancellationToken);

    /// <summary>The account's language or admin rights changed: its open tabs show the site anew.</summary>
    public Task AccountChangedAsync(Guid accountId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.AccountChanged, new AccountEvent(accountId), LiveJsonContext.Default.AccountEvent, cancellationToken);

    /// <summary>The account picked another colour scheme: its open tabs switch to it.</summary>
    public Task ThemeChangedAsync(Guid accountId, string theme, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.ThemeChanged, new ThemeChangedEvent(accountId, theme), LiveJsonContext.Default.ThemeChangedEvent, cancellationToken);

    /// <summary>The search index was written: open search pages ask again.</summary>
    public Task SearchChangedAsync(CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.SearchChanged, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, cancellationToken);

    public Task AdminChangedAsync(CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.AdminChanged, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, cancellationToken);

    /// <summary>Beeps came back (a ban lifted, a hidden beep restored): every page listing beeps catches up.</summary>
    public Task ResyncAsync(CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.Resync, new EmptyMessage(), LiveJsonContext.Default.EmptyMessage, cancellationToken);

    public Task ActorChangedAsync(Guid actorId, CancellationToken cancellationToken) =>
        AppendAsync(LiveEventTypes.ActorChanged, new ActorChangedEvent(actorId), LiveJsonContext.Default.ActorChangedEvent, cancellationToken);

    public Task CloseConnectionsAsync(IReadOnlyList<string> connectionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionIds);
        return connectionIds.Count == 0
            ? Task.CompletedTask
            : AppendAsync(LiveEventTypes.Close, new CloseEvent(connectionIds), LiveJsonContext.Default.CloseEvent, cancellationToken);
    }

    /// <summary>
    /// Counts are not published one change at a time: a popular beep would flood every page showing it.
    /// They are noted here and sent together by <see cref="PublishCountsAsync"/> every few seconds (PLAN 6.10).
    /// </summary>
    public Task CountsChangedAsync(Guid beepId, CancellationToken cancellationToken) => CountsChangedAsync([beepId], cancellationToken);

    /// <summary>As <see cref="CountsChangedAsync(Guid, CancellationToken)"/>, for many beeps in one round trip.</summary>
    public async Task CountsChangedAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        try
        {
            await store.MarkCountsChangedAsync(beepIds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (publishFailures.Allow())
            {
                LogPublishFailed(logger, exception, LiveEventTypes.Counts);
            }
        }
    }

    /// <summary>Publishes the current counts of beeps that changed since the last run. Instances may run it at once: each takes different beeps.</summary>
    /// <returns>The beeps taken; <see cref="CountsBatchSize"/> of them means there may be more.</returns>
    public async Task<IReadOnlyList<Guid>> PublishCountsAsync(CancellationToken cancellationToken)
    {
        var changed = await store.TakeChangedCountsAsync(CountsBatchSize, cancellationToken);
        if (changed.Count == 0)
        {
            return changed;
        }

        try
        {
            var counts = await counters.GetAsync(changed, cancellationToken);
            if (counts.Count > 0)
            {
                var entries = counts.Select(c => new CountsEntry(c.Key, Math.Max(0, c.Value.Likes), Math.Max(0, c.Value.Replies))).ToList();
                await store.AppendAsync(LiveEventTypes.Counts, JsonSerializer.Serialize(new CountsEvent(entries), LiveJsonContext.Default.CountsEvent), cancellationToken);
            }
        }
        catch
        {
            // Taken but not published: they go back for the next run, or pages keep the old counts until the next change.
            await CountsChangedAsync(changed, CancellationToken.None);
            throw;
        }

        return changed;
    }

    private async Task AppendAsync<T>(string type, T payload, JsonTypeInfo<T> payloadType, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendAsync(type, JsonSerializer.Serialize(payload, payloadType), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (publishFailures.Allow())
            {
                LogPublishFailed(logger, exception, type);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a live {EventType} event failed; open pages miss this update (logged at most once a minute)")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string eventType);
}
