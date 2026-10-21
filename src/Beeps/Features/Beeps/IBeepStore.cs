using Beeps.Features.Media;
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

    /// <summary>The ones among these beeps, not deleted, that have no text and no image that is or may become ready.</summary>
    Task<IReadOnlyList<Guid>> ListEmptyAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>The visible ones among these beeps, in no particular order.</summary>
    Task<IReadOnlyList<BeepRow>> GetRowsAsync(IReadOnlyCollection<Guid> beepIds, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>The visible actors each beep mentions, keyed by beep id (beeps without any are left out).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActorRow>>> GetMentionsAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>The ready images attached to each beep, in order, keyed by beep id (beeps without any are left out).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaRow>>> GetMediaAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);

    /// <summary>A thread oldest first: every visible beep with this root (the root included), after the cursor.</summary>
    Task<CursorPage<BeepRow>> ListThreadAsync(Guid rootId, Guid? after, int limit, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>Whether the human <paramref name="blockerId"/> blocked the actor or the human behind it (<paramref name="accountId"/>).</summary>
    Task<bool> IsBlockedByAsync(Guid blockerId, Guid actorId, Guid accountId, CancellationToken cancellationToken);

    /// <summary>Visible actors that have a handle (humans, connected agents) among these normalized handles.</summary>
    Task<IReadOnlyList<ActorRow>> FindActorsByHandlesAsync(IReadOnlyCollection<string> normalizedHandles, CancellationToken cancellationToken);

    /// <summary>Creates the hashtags that do not exist yet; returns the id of every name. Runs immediately.</summary>
    Task<IReadOnlyDictionary<string, Guid>> EnsureHashtagsAsync(IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Stages the beep with its mention and hashtag rows.</summary>
    void AddBeep(Beep beep, IReadOnlyCollection<Guid> mentionedActorIds, IReadOnlyCollection<Guid> hashtagIds);

    /// <summary>Stages the beep's images, in this order.</summary>
    void AttachMedia(Guid beepId, IReadOnlyList<Guid> mediaIds);

    /// <summary>
    /// Soft-deletes the beep. Runs immediately, locking the row until the transaction ends; returns the moderation
    /// status it had then, or null if it was already deleted.
    /// </summary>
    Task<ModerationStatus?> DeleteAsync(Guid beepId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if the actor already liked the beep.</summary>
    Task<bool> AddLikeAsync(Guid actorId, Guid beepId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if there was no like.</summary>
    Task<bool> RemoveLikeAsync(Guid actorId, Guid beepId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts a view of the beep in the human's history, unless they last viewed it after <paramref name="repeatAfter"/>
    /// (the same view still). Runs immediately; false when it was not counted. The beep's own count is
    /// <see cref="AddViewsAsync"/>'s.
    /// </summary>
    Task<bool> RecordViewAsync(Guid userId, Guid beepId, DateTimeOffset now, DateTimeOffset repeatAfter, CancellationToken cancellationToken);

    /// <summary>Adds views to the beeps' counts, in one statement. Runs immediately.</summary>
    Task AddViewsAsync(IReadOnlyDictionary<Guid, long> views, CancellationToken cancellationToken);

    /// <summary>
    /// Recounts likes and replies from their rows and writes them to the beeps' count columns. Runs
    /// immediately; returns the new counts (beeps that no longer exist are left out).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, BeepCounts>> RecountAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken);
}

/// <summary>
/// Live like and reply counts: a Valkey hash per beep, ahead of the count columns, which
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

    /// <summary>
    /// Adds a view to those waiting to be written to the beep's column (<see cref="BeepCounterFlushJob"/>), so a viral beep's
    /// row is not updated on every view. False when as many beeps as allowed are waiting already: count it there at once.
    /// </summary>
    Task<bool> AddViewAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>Removes and returns the views waiting to be written, per beep.</summary>
    Task<IReadOnlyDictionary<Guid, long>> TakeViewsAsync(CancellationToken cancellationToken);

    /// <summary>Puts taken views back (after a failed write).</summary>
    Task ReturnViewsAsync(IReadOnlyDictionary<Guid, long> views, CancellationToken cancellationToken);

    /// <summary>Replaces the live counts with recounted ones.</summary>
    Task SetAsync(IReadOnlyDictionary<Guid, BeepCounts> counts, CancellationToken cancellationToken);
}
