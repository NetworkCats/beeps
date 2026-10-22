using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Beeps.Infrastructure.Caching;

public enum CacheChangeKind
{
    /// <summary>What a beep shows changed (its images, its translations): its entry and the pages showing it.</summary>
    Beep,

    /// <summary>A beep was deleted: its entry and the pages showing it.</summary>
    BeepRemoved,

    /// <summary>A beep was hidden by moderation: the pages showing it (whether it is hidden is never cached).</summary>
    BeepHidden,

    /// <summary>A beep was posted: the pages it belongs on (its thread, profiles, hashtags, the public timeline).</summary>
    BeepPosted,

    /// <summary>A beep's like or reply count changed: the pages showing it.</summary>
    BeepCounts,

    /// <summary>An actor's name, handle, bio or avatar changed: its entry and the pages showing it.</summary>
    Actor,

    /// <summary>An agent connected (got its name and handle): its entry, and its owner's profile, which now lists it.</summary>
    AgentConnected,

    /// <summary>An actor was deleted or banned: its entry and the pages showing it or (for a human) its agents.</summary>
    ActorVisibility,

    /// <summary>A ban was lifted: its entry, and every cached page, as its beeps belong back on any of them.</summary>
    ActorRestored,

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

    public static CacheChange BeepHidden(Guid id) => new(CacheChangeKind.BeepHidden, id);

    public static CacheChange BeepPosted(Guid id) => new(CacheChangeKind.BeepPosted, id);

    public static CacheChange BeepCounts(Guid id) => new(CacheChangeKind.BeepCounts, id);

    public static CacheChange Actor(Guid id) => new(CacheChangeKind.Actor, id);

    public static CacheChange AgentConnected(Guid id) => new(CacheChangeKind.AgentConnected, id);

    public static CacheChange ActorVisibility(Guid id) => new(CacheChangeKind.ActorVisibility, id);

    public static CacheChange ActorRestored(Guid id) => new(CacheChangeKind.ActorRestored, id);

    public static CacheChange Handle(string normalizedHandle) => new(CacheChangeKind.Handle, Guid.Empty, normalizedHandle);

    public static CacheChange Following(Guid humanId) => new(CacheChangeKind.Following, humanId);

    public static CacheChange Agents(Guid ownerId) => new(CacheChangeKind.Agents, ownerId);
}

/// <summary>Outbox payload: the durable copy of a committed change's invalidations (<see cref="PendingCacheChanges"/>).</summary>
public sealed record CacheInvalidationRequest(IReadOnlyList<CacheChange> Changes)
{
    public const string MessageType = "cache.invalidate";
}

/// <summary>Outbox payload: sign-in entries to drop from Valkey (<see cref="SharedCacheForgetHandler"/>).</summary>
public sealed record SharedCacheForgetRequest(IReadOnlyList<string> Keys)
{
    public const string MessageType = "cache.forget_shared";
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
        nameof(Features.Accounts.Actor.Website),
        nameof(Features.Accounts.Actor.ModelName),
        nameof(Features.Accounts.Actor.AvatarMediaId),
        nameof(Features.Accounts.Actor.BannerMediaId),
    ];

    /// <summary>What a human's profile shows of them to others.</summary>
    private static readonly string[] PrivacyProperties =
    [
        nameof(User.HideLikes),
        nameof(User.HideFollows),
        nameof(User.HideFollowCounts),
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
                    pending.Add(user.BannedAt is null ? CacheChange.ActorRestored(user.Id) : CacheChange.ActorVisibility(user.Id));
                    break;
                // Their profile shows more or less of them now.
                case User user when entry.State == EntityState.Modified && PrivacyProperties.Any(p => entry.Property(p).IsModified):
                    pending.Add(CacheChange.Actor(user.Id));
                    break;
                case Beep beep when entry.State == EntityState.Added:
                    pending.Add(CacheChange.BeepPosted(beep.Id));
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

    /// <summary>After a rollback or a failed save: nothing changed.</summary>
    public void Discard()
    {
        pending.Clear();
        // A failed save leaves the staged copy tracked as added, and the next save would commit it.
        foreach (var entry in db.ChangeTracker.Entries<OutboxMessage>().Where(e => e.State == EntityState.Added && e.Entity.Type == CacheInvalidationRequest.MessageType).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

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
