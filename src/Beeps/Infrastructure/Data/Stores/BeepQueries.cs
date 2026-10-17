using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>The building blocks every store uses to read beeps, so visibility and the row shape are applied the same way everywhere.</summary>
internal static class BeepQueries
{
    /// <summary>Beeps the viewer may see (<see cref="BeepVisibility"/>), untracked.</summary>
    public static IQueryable<Beep> Visible(BeepsDbContext db, Guid? viewerId) =>
        db.Set<Beep>().AsNoTracking().VisibleTo(viewerId, db.Set<Actor>(), db.Set<User>(), db.Set<Block>());

    public static IQueryable<Actor> VisibleActors(BeepsDbContext db) =>
        db.Set<Actor>().AsNoTracking().VisibleActors(db.Set<User>());

    /// <summary>Visible actors the viewer has not blocked (<see cref="BeepVisibility.NotBlockedBy"/>).</summary>
    public static IQueryable<Actor> VisibleActors(BeepsDbContext db, Guid? viewerId) =>
        VisibleActors(db).NotBlockedBy(viewerId, db.Set<Actor>(), db.Set<Block>());

    /// <summary>
    /// What a query needs to say about each beep: its id and what depends on the reader. The rest is the same for
    /// everyone and comes from the content cache (<see cref="ContentReads.ToRowsAsync"/>).
    /// </summary>
    public static IQueryable<BeepHit> Hits(this IQueryable<Beep> beeps, BeepsDbContext db, Guid? viewerId) =>
        beeps.Select(b => new BeepHit(
            b.Id,
            viewerId != null && db.Set<Like>().Any(l => l.BeepId == b.Id && l.ActorId == viewerId),
            b.ModerationStatus == ModerationStatus.Hidden));

    /// <summary>One newest-first page, cut by <see cref="PageRequest.Before"/> and <see cref="PageRequest.SinceId"/>.</summary>
    public static async Task<CursorPage<BeepRow>> NewestFirstAsync(
        this IQueryable<Beep> beeps, BeepsDbContext db, ContentReads reads, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        if (page.Before is { } before)
        {
            beeps = beeps.Where(b => b.Id < before);
        }

        if (page.SinceId is { } since)
        {
            beeps = beeps.Where(b => b.Id > since);
        }

        var hits = await beeps
            .OrderByDescending(b => b.Id)
            .Take(page.Limit + 1)
            .Hits(db, viewerId)
            .ToListAsync(cancellationToken);
        return await ToPageAsync(reads, hits, page.Limit, cancellationToken);
    }

    /// <summary>A page from <c>limit + 1</c> hits: the cursor comes from the hits, so a beep deleted meanwhile does not shift it.</summary>
    public static async Task<CursorPage<BeepRow>> ToPageAsync(ContentReads reads, IReadOnlyList<BeepHit> hits, int limit, CancellationToken cancellationToken)
    {
        var page = CursorPage.FromOverfetch(hits, limit, h => h.Id);
        return new CursorPage<BeepRow>(await reads.ToRowsAsync(page.Items, cancellationToken), page.NextCursor);
    }
}
