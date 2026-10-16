using Beeps.Features.Media;
using System.Linq.Expressions;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>The building blocks every store uses to read beeps, so visibility and the row shape are applied the same way everywhere.</summary>
internal static class BeepQueries
{
    public static readonly Expression<Func<Actor, ActorRow>> ToActorRow =
        a => new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId, a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey);

    /// <summary>Beeps the viewer may see (<see cref="BeepVisibility"/>), untracked.</summary>
    public static IQueryable<Beep> Visible(BeepsDbContext db, Guid? viewerId) =>
        db.Set<Beep>().AsNoTracking().VisibleTo(viewerId, db.Set<Actor>(), db.Set<User>(), db.Set<Block>());

    public static IQueryable<Actor> VisibleActors(BeepsDbContext db) =>
        db.Set<Actor>().AsNoTracking().VisibleActors(db.Set<User>());

    /// <summary>Visible actors the viewer has not blocked (<see cref="BeepVisibility.NotBlockedBy"/>).</summary>
    public static IQueryable<Actor> VisibleActors(BeepsDbContext db, Guid? viewerId) =>
        VisibleActors(db).NotBlockedBy(viewerId, db.Set<Actor>(), db.Set<Block>());

    public static IQueryable<BeepRow> Rows(this IQueryable<Beep> beeps, BeepsDbContext db, Guid? viewerId) =>
        beeps.Select(b => new BeepRow(
            b.Id,
            b.Text,
            new ActorRow(
                b.Author.Id,
                b.Author.Kind,
                b.Author.Handle,
                b.Author.DisplayName,
                b.Author.OwnerId,
                b.Author.AvatarMedia == null || b.Author.AvatarMedia.Status != MediaStatus.Ready ? null : b.Author.AvatarMedia.StorageKey),
            b.ReplyToId,
            b.RootId,
            b.CreatedAt,
            b.LikeCount,
            b.ReplyCount,
            viewerId != null && db.Set<Like>().Any(l => l.BeepId == b.Id && l.ActorId == viewerId),
            b.ModerationStatus == ModerationStatus.Hidden));

    /// <summary>One newest-first page, cut by <see cref="PageRequest.Before"/> and <see cref="PageRequest.SinceId"/>.</summary>
    public static async Task<CursorPage<BeepRow>> NewestFirstAsync(
        this IQueryable<Beep> beeps, BeepsDbContext db, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        if (page.Before is { } before)
        {
            beeps = beeps.Where(b => b.Id < before);
        }

        if (page.SinceId is { } since)
        {
            beeps = beeps.Where(b => b.Id > since);
        }

        var rows = await beeps
            .OrderByDescending(b => b.Id)
            .Take(page.Limit + 1)
            .Rows(db, viewerId)
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, page.Limit, r => r.Id);
    }
}
