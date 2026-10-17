using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Beeps;

namespace Beeps.Features.Live;

/// <summary>
/// Publishes live events (PLAN 12: every change that alters a page publishes one). Called after the change is
/// committed; live updates are best effort, so a Valkey outage is logged and never fails the change itself.
/// </summary>
public sealed partial class LiveUpdates(ILiveEventStore store, IBeepCounterStore counters, ILogger<LiveUpdates> logger)
{
    /// <summary>Beeps per merged counts event.</summary>
    public const int CountsBatchSize = 500;

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
    public async Task CountsChangedAsync(Guid beepId, CancellationToken cancellationToken)
    {
        try
        {
            await store.MarkCountsChangedAsync(beepId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPublishFailed(logger, exception, LiveEventTypes.Counts);
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

        var counts = await counters.GetAsync(changed, cancellationToken);
        if (counts.Count > 0)
        {
            var entries = counts.Select(c => new CountsEntry(c.Key, Math.Max(0, c.Value.Likes), Math.Max(0, c.Value.Replies))).ToList();
            await store.AppendAsync(LiveEventTypes.Counts, JsonSerializer.Serialize(new CountsEvent(entries), LiveJsonContext.Default.CountsEvent), cancellationToken);
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
            LogPublishFailed(logger, exception, type);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a live {EventType} event failed; open pages miss this update")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string eventType);
}
