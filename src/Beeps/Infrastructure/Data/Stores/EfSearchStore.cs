using Beeps.Features.Media;
using System.Linq.Expressions;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Search;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSearchStore(BeepsDbContext db) : ISearchStore
{
    public async Task<BeepDocument?> FindBeepAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var row = await BeepQueries.Visible(db, null).Where(b => b.Id == beepId).Select(ToRow).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Document(row);
    }

    public async Task<IReadOnlyList<ActorDocumentState>> GetAccountAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var findable = Findable();
        var actors = await db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.Id == actorId || a.OwnerId == actorId)
            .Select(a => new { a.Id, a.Kind, a.Handle, a.DisplayName, a.Bio, Findable = findable.Any(f => f.Id == a.Id) })
            .ToListAsync(cancellationToken);
        return [.. actors.Select(a => new ActorDocumentState(a.Id, a.Findable ? new ActorDocument(a.Id, a.Kind, a.Handle!, a.DisplayName, a.Bio) : null))];
    }

    public async Task<IReadOnlyList<BeepDocument>> ListBeepsAsync(IReadOnlyCollection<Guid>? authorIds, Guid? after, int limit, CancellationToken cancellationToken)
    {
        var beeps = BeepQueries.Visible(db, null);
        if (authorIds is not null)
        {
            beeps = beeps.Where(b => authorIds.Contains(b.AuthorId));
        }

        if (after is { } cursor)
        {
            beeps = beeps.Where(b => b.Id > cursor);
        }

        var rows = await beeps.OrderBy(b => b.Id).Take(limit).Select(ToRow).ToListAsync(cancellationToken);
        return [.. rows.Select(Document)];
    }

    public async Task<IReadOnlyList<ActorDocument>> ListActorsAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        var actors = Findable();
        if (after is { } cursor)
        {
            actors = actors.Where(a => a.Id > cursor);
        }

        return await actors.OrderBy(a => a.Id).Take(limit).Select(a => new ActorDocument(a.Id, a.Kind, a.Handle!, a.DisplayName, a.Bio)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActorSearchRow>> GetActorsAsync(IReadOnlyCollection<Guid> actorIds, CancellationToken cancellationToken) =>
        actorIds.Count == 0
            ? []
            : await Findable()
                .Where(a => actorIds.Contains(a.Id))
                .Select(a => new ActorSearchRow(
                    new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId, a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey),
                    a.Bio))
                .ToListAsync(cancellationToken);

    /// <summary>
    /// Who can be found: visible actors with a handle (agents get one when they connect). People only once they
    /// verified their email, so sign-ups that never finish do not fill the results.
    /// </summary>
    private IQueryable<Actor> Findable()
    {
        var verified = db.Set<User>().Where(u => u.EmailVerifiedAt != null).Select(u => u.Id);
        return BeepQueries.VisibleActors(db).Where(a => a.Handle != null && (a.Kind == ActorKind.Agent || verified.Contains(a.Id)));
    }

    private static readonly Expression<Func<Beep, BeepTextRow>> ToRow = b => new BeepTextRow(b.Id, b.Text, b.AuthorId, b.CreatedAt);

    private static BeepDocument Document(BeepTextRow row) => new(row.Id, row.Text, row.AuthorId, BeepText.Hashtags(row.Text), row.CreatedAt);

    private sealed record BeepTextRow(Guid Id, string Text, Guid AuthorId, DateTimeOffset CreatedAt);
}
