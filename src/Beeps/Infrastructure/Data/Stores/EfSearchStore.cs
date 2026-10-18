using Beeps.Features.Media;
using System.Linq.Expressions;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Search;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfSearchStore(BeepsDbContext db, ContentReads reads) : ISearchStore
{
    private const string ByteOrder = "C";

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

    /// <remarks>
    /// A one-letter prefix matches a good share of everyone, so neither part reads all the matches. The people the viewer is
    /// close to (their agents, the humans they follow and those humans' agents) come from the cached lists, by primary key.
    /// The rest come shortest handle first off <c>ix_actors_handle_length</c> (<c>length(normalized_handle),
    /// normalized_handle COLLATE "C"</c>): one short walk per handle length (LATERAL), each stopping once the page is full.
    /// Handles of one length are in byte order ("C"), which is what lets the index give them in order.
    /// </remarks>
    public async Task<IReadOnlyList<ActorRow>> SuggestActorsAsync(string normalizedPrefix, Viewer viewer, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalizedPrefix);
        ArgumentNullException.ThrowIfNull(viewer);
        var pattern = SqlLike.Prefix(normalizedPrefix);
        var matching = Findable(viewer.ActorId)
            .Where(a => a.Id != viewer.ActorId && a.NormalizedHandle != null && EF.Functions.Like(EF.Functions.Collate(a.NormalizedHandle, ByteOrder), pattern, SqlLike.Escape));

        // Following a human counts for their agents too.
        var followed = await reads.GetFollowedAsync(viewer.AccountId, cancellationToken);
        var agents = await reads.GetAgentIdsAsync([viewer.AccountId, .. followed], cancellationToken);
        Guid[] close = [.. agents.Values.SelectMany(a => a), .. followed];
        List<ActorRow> found = close.Length == 0
            ? []
            : await matching
                .Where(a => close.Contains(a.Id))
                .OrderByDescending(a => a.OwnerId == viewer.AccountId)
                .ThenBy(a => a.NormalizedHandle!.Length)
                .ThenBy(a => EF.Functions.Collate(a.NormalizedHandle!, ByteOrder))
                .Take(limit)
                .Select(ToActorRow)
                .ToListAsync(cancellationToken);
        if (found.Count >= limit)
        {
            return found;
        }

        // As many again as were found already, which may come up here too.
        var take = limit + found.Count;
        var lengths = db.Database.SqlQuery<int>($"SELECT generate_series({normalizedPrefix.Length}, {Actor.HandleMaxLength}) AS \"Value\"");
        var rest = await lengths
            .SelectMany(length => matching.Where(a => a.NormalizedHandle!.Length == length).OrderBy(a => EF.Functions.Collate(a.NormalizedHandle!, ByteOrder)).Take(take))
            .OrderBy(a => a.NormalizedHandle!.Length)
            .ThenBy(a => EF.Functions.Collate(a.NormalizedHandle!, ByteOrder))
            .Take(take)
            .Select(ToActorRow)
            .ToListAsync(cancellationToken);
        var shown = found.Select(a => a.Id).ToHashSet();
        return [.. found, .. rest.Where(a => !shown.Contains(a.Id)).Take(limit - found.Count)];
    }

    /// <summary>
    /// Who can be found: visible actors with a handle (agents get one when they connect), and none the viewer blocked.
    /// People only once they verified their email, so sign-ups that never finish do not fill the results.
    /// </summary>
    /// <remarks>
    /// The check is a primary-key probe per actor: as <c>IN (verified users)</c>, PostgreSQL hashed every verified user
    /// for each query.
    /// </remarks>
    private IQueryable<Actor> Findable(Guid? viewerId = null)
    {
        var users = db.Set<User>();
        return BeepQueries.VisibleActors(db, viewerId)
            .Where(a => a.Handle != null && (a.Kind == ActorKind.Agent || users.Any(u => u.Id == a.Id && u.EmailVerifiedAt != null)));
    }

    private static readonly Expression<Func<Actor, ActorRow>> ToActorRow = a =>
        new ActorRow(a.Id, a.Kind, a.Handle, a.DisplayName, a.OwnerId, a.AvatarMedia == null || a.AvatarMedia.Status != MediaStatus.Ready ? null : a.AvatarMedia.StorageKey);

    private static readonly Expression<Func<Beep, BeepTextRow>> ToRow = b => new BeepTextRow(b.Id, b.Text, b.AuthorId, b.CreatedAt);

    private static BeepDocument Document(BeepTextRow row) => new(row.Id, row.Text, row.AuthorId, BeepText.Hashtags(row.Text), row.CreatedAt);

    private sealed record BeepTextRow(Guid Id, string Text, Guid AuthorId, DateTimeOffset CreatedAt);
}
