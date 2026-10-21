namespace Beeps.Features.Live;

/// <summary>
/// The shared event stream and the bookkeeping around it, in Valkey. All of it is
/// short-lived: losing it only means pages miss some live updates until they are reloaded.
/// </summary>
public interface ILiveEventStore
{
    /// <summary>Adds an event to the stream, which keeps only a bounded number of recent ones.</summary>
    Task AppendAsync(string type, string data, CancellationToken cancellationToken);

    /// <summary>
    /// Calls <paramref name="appended"/> whenever any instance adds an event. Only a hint: Pub/Sub keeps nothing for a
    /// subscriber that is away, so readers still look at the stream now and then without one.
    /// </summary>
    Task SubscribeAppendedAsync(Action appended, CancellationToken cancellationToken);

    /// <summary>Entries newer than <paramref name="after"/>, oldest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<LiveEvent>> ReadAfterAsync(LiveEventId after, int max, CancellationToken cancellationToken);

    /// <summary>The newest entry trimmed off the stream to keep it short; null if none was (or there is no stream).</summary>
    Task<LiveEventId?> TrimmedUpToAsync(CancellationToken cancellationToken);

    /// <summary>The newest entry; null when the stream is empty.</summary>
    Task<LiveEventId?> LastIdAsync(CancellationToken cancellationToken);

    /// <summary>Notes that the beeps' live counts changed, for the next merged <see cref="LiveEventTypes.Counts"/> event. One call for all of them.</summary>
    Task MarkCountsChangedAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>Removes and returns up to <paramref name="max"/> beeps whose counts changed.</summary>
    Task<IReadOnlyList<Guid>> TakeChangedCountsAsync(int max, CancellationToken cancellationToken);

    /// <summary>
    /// Counts an open connection of the human or agent. Returns the ids of its oldest connections beyond
    /// <paramref name="max"/>, which stop being counted and should be closed.
    /// </summary>
    Task<IReadOnlyList<string>> AddConnectionAsync(Guid actorId, string connectionId, DateTimeOffset now, int max, CancellationToken cancellationToken);

    Task RemoveConnectionAsync(Guid actorId, string connectionId, CancellationToken cancellationToken);
}

/// <summary>This instance's view of the stream: every connection subscribes here instead of reading Valkey itself.</summary>
public interface ILiveEventFeed
{
    /// <summary>
    /// Starts receiving events. With <paramref name="after"/> the events after it come first (replay); if some
    /// of them are gone, a <see cref="LiveEventTypes.Gap"/> event comes instead.
    /// </summary>
    ILiveSubscription Subscribe(LiveEventId? after);
}

public interface ILiveSubscription : IDisposable
{
    /// <summary>Ends when the subscriber falls too far behind; the client then reconnects and replays.</summary>
    IAsyncEnumerable<LiveEvent> ReadAllAsync(CancellationToken cancellationToken);
}
