using Beeps.Features.Shared;

namespace Beeps.Features.Beeps;

/// <summary>
/// Beep data access. Every read applies <see cref="BeepVisibility"/> for the given viewer (null when signed
/// out). Staged writes are committed by <see cref="IUnitOfWork"/>; methods documented as "runs immediately"
/// execute their SQL at once (inside the caller's transaction, if any).
/// </summary>
public interface IBeepStore
{
    Task<BeepRef?> FindVisibleAsync(Guid beepId, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>The visible ones among these beeps, in no particular order.</summary>
    Task<IReadOnlyList<BeepRow>> GetRowsAsync(IReadOnlyCollection<Guid> beepIds, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>The visible actors each beep mentions, keyed by beep id (beeps without any are left out).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActorRow>>> GetMentionsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>A thread oldest first: every visible beep with this root (the root included), after the cursor.</summary>
    Task<CursorPage<BeepRow>> ListThreadAsync(Guid rootId, Guid? after, int limit, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>Visible actors that have a handle (humans, connected agents) among these normalized handles.</summary>
    Task<IReadOnlyList<ActorRow>> FindActorsByHandlesAsync(IReadOnlyCollection<string> normalizedHandles, CancellationToken cancellationToken);

    /// <summary>Creates the hashtags that do not exist yet; returns the id of every name. Runs immediately.</summary>
    Task<IReadOnlyDictionary<string, Guid>> EnsureHashtagsAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Stages the beep with its mention and hashtag rows.</summary>
    void AddBeep(Beep beep, IReadOnlyCollection<Guid> mentionedActorIds, IReadOnlyCollection<Guid> hashtagIds);

    /// <summary>Soft-deletes the beep. Runs immediately; false if it was already deleted.</summary>
    Task<bool> DeleteAsync(Guid beepId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if the actor already liked the beep.</summary>
    Task<bool> AddLikeAsync(Guid actorId, Guid beepId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if there was no like.</summary>
    Task<bool> RemoveLikeAsync(Guid actorId, Guid beepId, CancellationToken cancellationToken);

    /// <summary>
    /// Recounts likes and replies from their rows and writes them to the beeps' count columns. Runs
    /// immediately; returns the new counts (beeps that no longer exist are left out).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, BeepCounts>> RecountAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);
}

/// <summary>
/// Live like and reply counts (PLAN 6.6): a Valkey hash per beep, ahead of the count columns, which
/// <see cref="BeepCounterFlushJob"/> brings up to date. Everything here can be rebuilt from the database.
/// </summary>
public interface IBeepCounterStore
{
    /// <summary>The live counts of the beeps that have them; the others fall back to their columns.</summary>
    Task<IReadOnlyDictionary<Guid, BeepCounts>> GetAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>
    /// Adds <paramref name="delta"/> to the live counts, starting from <paramref name="baseline"/> (the
    /// columns) when there are none yet, and marks the beep for write-back.
    /// </summary>
    Task AddAsync(Guid beepId, BeepCounts baseline, BeepCounts delta, CancellationToken cancellationToken);

    /// <summary>Removes and returns up to <paramref name="max"/> beeps marked for write-back.</summary>
    Task<IReadOnlyList<Guid>> TakeDirtyAsync(int max, CancellationToken cancellationToken);

    /// <summary>Marks beeps for write-back again (after a failed flush).</summary>
    Task MarkDirtyAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>Replaces the live counts with recounted ones.</summary>
    Task SetAsync(IReadOnlyDictionary<Guid, BeepCounts> counts, CancellationToken cancellationToken);
}
