using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Beeps.Infrastructure.Caching;

public enum CacheChangeKind
{
    /// <summary>What a beep shows changed (its images): its entry.</summary>
    Beep,

    /// <summary>A beep was deleted: its entry, and the cached pages that may show it.</summary>
    BeepRemoved,

    /// <summary>
    /// A beep was posted, or hidden by moderation: the cached pages that show it, or should (its entry, if any, stays
    /// right).
    /// </summary>
    BeepPages,

    /// <summary>An actor's name, handle, bio or avatar changed: its entry and its profile pages.</summary>
    Actor,

    /// <summary>An actor was deleted, banned or unbanned: its entry, and every cached page, as its beeps may be on any.</summary>
    ActorVisibility,

    /// <summary>A handle now points elsewhere (or nowhere): <see cref="CacheChange.Name"/> is the normalized handle.</summary>
    Handle,

    /// <summary>Whom a human follows.</summary>
    Following,

    /// <summary>Which agents a human has.</summary>
    Agents,
}

/// <param name="Id">The beep, actor or human; empty for <see cref="CacheChangeKind.Handle"/>.</param>
public readonly record struct CacheChange(CacheChangeKind Kind, Guid Id, string? Name = null)
{
    public static CacheChange Beep(Guid id) => new(CacheChangeKind.Beep, id);

    public static CacheChange BeepRemoved(Guid id) => new(CacheChangeKind.BeepRemoved, id);

    public static CacheChange BeepPages(Guid id) => new(CacheChangeKind.BeepPages, id);

    public static CacheChange Actor(Guid id) => new(CacheChangeKind.Actor, id);

    public static CacheChange ActorVisibility(Guid id) => new(CacheChangeKind.ActorVisibility, id);

    public static CacheChange Handle(string normalizedHandle) => new(CacheChangeKind.Handle, Guid.Empty, normalizedHandle);

    public static CacheChange Following(Guid humanId) => new(CacheChangeKind.Following, humanId);

    public static CacheChange Agents(Guid ownerId) => new(CacheChangeKind.Agents, ownerId);
}

/// <summary>Outbox payload: the durable copy of a committed change's invalidations (<see cref="PendingCacheChanges"/>).</summary>
public sealed record CacheInvalidationRequest(IReadOnlyList<CacheChange> Changes)
{
    public const string MessageType = "cache.invalidate";
}

/// <summary>
/// The cache invalidations of the request's database work, held until it commits. Stores note what their
/// immediate writes changed; <see cref="EfUnitOfWork"/> notes what tracked entities changed, hands everything to
/// <see cref="CacheInvalidator"/> after the commit and drops it on a rollback. Invalidating before the commit would
/// let a concurrent read put the old row straight back.
/// </summary>
/// <remarks>
/// Every batch is also written to the outbox in the same transaction (<see cref="StageDurableCopy"/>): the in-process
/// queue is what makes invalidation immediate, the outbox message what makes it certain, even if this instance stops
/// right after the commit. Running a batch twice is harmless.
/// </remarks>
public sealed class PendingCacheChanges(BeepsDbContext db, CacheInvalidator invalidator, IOutbox outbox)
{
    private static readonly string[] ShownActorProperties =
    [
        nameof(Features.Accounts.Actor.Handle),
        nameof(Features.Accounts.Actor.DisplayName),
        nameof(Features.Accounts.Actor.Bio),
        nameof(Features.Accounts.Actor.ModelName),
        nameof(Features.Accounts.Actor.AvatarMediaId),
    ];

    private readonly HashSet<CacheChange> pending = [];

    public bool HasPending => pending.Count > 0;

    /// <summary>
    /// For writes that already ran, inside a transaction (sent after the commit). Stores only write outside one when
    /// what they change is cached nowhere; this throws otherwise, as the change would have no durable copy.
    /// </summary>
    public void Add(params IEnumerable<CacheChange> changes)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A write that changes cached content must run in a transaction (IUnitOfWork.InTransactionAsync).");
        }

        pending.UnionWith(changes);
    }

    /// <summary>Stages the outbox copy of what is pending, to be saved and committed with the change. Call before that save.</summary>
    public void StageDurableCopy()
    {
        if (pending.Count > 0)
        {
            outbox.Enqueue(CacheInvalidationRequest.MessageType, new CacheInvalidationRequest([.. pending]), ContentCacheJsonContext.Default.CacheInvalidationRequest);
        }
    }

    /// <summary>Notes what the tracked entities about to be saved change. Call right before saving them.</summary>
    public void CaptureTracked()
    {
        db.ChangeTracker.DetectChanges();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Actor actor:
                    Capture(entry, actor);
                    break;
                case User user when entry.State == EntityState.Modified && entry.Property(nameof(User.BannedAt)).IsModified:
                    pending.Add(CacheChange.ActorVisibility(user.Id));
                    break;
                case Beep beep when entry.State == EntityState.Added:
                    pending.Add(CacheChange.BeepPages(beep.Id));
                    break;
                case Beep beep when entry.State == EntityState.Modified && entry.Property(nameof(Beep.DeletedAt)).IsModified:
                    pending.Add(CacheChange.BeepRemoved(beep.Id));
                    break;
                case Follow follow when entry.State is EntityState.Added or EntityState.Deleted:
                    pending.Add(CacheChange.Following(follow.FollowerId));
                    break;
            }
        }
    }

    /// <summary>After the commit (or a save outside any transaction).</summary>
    public void Flush()
    {
        if (pending.Count == 0)
        {
            return;
        }

        invalidator.Invalidate([.. pending]);
        pending.Clear();
    }

    /// <summary>After a rollback: nothing changed.</summary>
    public void Discard() => pending.Clear();

    private void Capture(EntityEntry entry, Actor actor)
    {
        if (entry.State == EntityState.Added)
        {
            if (actor.OwnerId is { } ownerId)
            {
                pending.Add(CacheChange.Agents(ownerId));
            }

            return;
        }

        if (entry.State != EntityState.Modified)
        {
            return;
        }

        if (entry.Property(nameof(Actor.DeletedAt)).IsModified)
        {
            pending.Add(CacheChange.ActorVisibility(actor.Id));
        }
        else if (ShownActorProperties.Any(p => entry.Property(p).IsModified))
        {
            pending.Add(CacheChange.Actor(actor.Id));
        }

        var handle = entry.Property(nameof(Actor.NormalizedHandle));
        if (handle.IsModified && handle.OriginalValue is string previous)
        {
            pending.Add(CacheChange.Handle(previous));
        }
    }
}
