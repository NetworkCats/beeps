using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>
/// Read-time timelines: the author set comes from the content cache and is passed as an array, the beeps come off
/// the <c>(author_id, id DESC)</c> index, and only their ids and the reader's own state are queried
/// (PLAN 6.6: the beeps themselves are cached on their own).
/// </summary>
public sealed class EfTimelineStore(BeepsDbContext db, ContentReads reads) : ITimelineStore
{
    public Task<CursorPage<BeepRow>> PublicAsync(PageRequest page, Guid? viewerId, CancellationToken cancellationToken) =>
        BeepQueries.Visible(db, viewerId)
            .Where(b => b.ReplyToId == null)
            .NewestFirstAsync(db, reads, page, viewerId, cancellationToken);

    /// <remarks>
    /// The authors are read first (from the cache) and passed as an array; see <see cref="NewestByAuthorsAsync"/>.
    /// Replies are checked against their parent by primary key, reply by reply: as a subquery, PostgreSQL hashed
    /// every visible beep of the whole author set instead (50 ms per page at a million beeps).
    /// </remarks>
    public async Task<CursorPage<BeepRow>> HomeAsync(Guid humanId, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        // Following a human means following all of their agents too (PLAN 6.6).
        List<Guid> humans = [humanId, .. await reads.GetFollowedAsync(humanId, cancellationToken)];
        var authors = await AuthorsAsync(humans, cancellationToken);
        // A reply belongs here only when it answers a visible beep by one of the same authors (PLAN 6.6).
        var parents = BeepQueries.Visible(db, viewerId).Where(p => authors.Contains(p.AuthorId));
        var beeps = Shown(viewerId, page)
            .LeftJoin(parents, b => b.ReplyToId, p => (Guid?)p.Id, (b, p) => new { Beep = b, Parent = p })
            .Where(x => x.Beep.ReplyToId == null || x.Parent != null)
            .Select(x => x.Beep);
        return await NewestByAuthorsAsync(authors, beeps, page, viewerId, cancellationToken);
    }

    public async Task<CursorPage<BeepRow>> ProfileAsync(Guid actorId, ProfileTimeline filter, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var authors = await AuthorsAsync([actorId], cancellationToken);
        var beeps = filter switch
        {
            ProfileTimeline.Beeps => Shown(viewerId, page).Where(b => b.ReplyToId == null),
            ProfileTimeline.Replies => Shown(viewerId, page).Where(b => b.ReplyToId != null),
            _ => Shown(viewerId, page),
        };
        return await NewestByAuthorsAsync(authors, beeps, page, viewerId, cancellationToken);
    }

    /// <remarks>
    /// The tag's id is read first (from the cache), and the query walks the <c>(hashtag_id, beep_id DESC)</c> index
    /// newest first, stopping once the page is full. Matching tagged beeps by id and then sorting them read every
    /// beep with the tag, which grows without bound for a popular one.
    /// </remarks>
    public async Task<CursorPage<BeepRow>> HashtagAsync(string normalizedTag, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        if (await reads.GetHashtagIdAsync(normalizedTag, cancellationToken) is not { } id)
        {
            return new CursorPage<BeepRow>([], null);
        }

        var tagged = db.Set<BeepHashtag>().Where(t => t.HashtagId == id);
        if (page.Before is { } before)
        {
            tagged = tagged.Where(t => t.BeepId < before);
        }

        if (page.SinceId is { } since)
        {
            tagged = tagged.Where(t => t.BeepId > since);
        }

        var hits = await tagged
            .Join(BeepQueries.Visible(db, viewerId), t => t.BeepId, b => b.Id, (t, b) => new { t.BeepId, Beep = b })
            .OrderByDescending(x => x.BeepId)
            .Take(page.Limit + 1)
            .Select(x => x.Beep)
            .Hits(db, viewerId)
            .ToListAsync(cancellationToken);
        return await BeepQueries.ToPageAsync(reads, hits, page.Limit, cancellationToken);
    }

    /// <summary>
    /// The newest of <paramref name="beeps"/> by any of <paramref name="authors"/> that the viewer may see: each
    /// author's own newest come off the <c>(author_id, id DESC)</c> index (a LATERAL join, one short index walk per
    /// author) and the page is the newest of those.
    /// </summary>
    /// <remarks>
    /// Filtering one newest-first walk over all beeps by author is fast only while the authors post often: for a
    /// home timeline of quiet accounts it read through the whole table to fill a page. This way the work is bounded
    /// by authors × page size, whoever they are. Whether the authors are visible is checked once per author.
    /// </remarks>
    private async Task<CursorPage<BeepRow>> NewestByAuthorsAsync(List<Guid> authors, IQueryable<Beep> beeps, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var take = page.Limit + 1;
        var hits = await BeepQueries.VisibleActors(db, viewerId)
            .Where(a => authors.Contains(a.Id))
            // "b.Id != a.Id" always holds (a beep is not its author). It is there because EF rewrites a correlation
            // made only of equalities into ROW_NUMBER() over every beep of the authors; anything else stays LATERAL.
            .SelectMany(a => beeps.Where(b => b.AuthorId == a.Id && b.Id != a.Id).OrderByDescending(b => b.Id).Take(take))
            .OrderByDescending(b => b.Id)
            .Take(take)
            .Hits(db, viewerId)
            .ToListAsync(cancellationToken);
        return await BeepQueries.ToPageAsync(reads, hits, page.Limit, cancellationToken);
    }

    /// <summary>What the viewer may see of beeps whose authors are checked separately, within the page's cursor.</summary>
    private IQueryable<Beep> Shown(Guid? viewerId, PageRequest page)
    {
        var beeps = db.Set<Beep>().AsNoTracking().Where(BeepVisibility.IsShownTo(viewerId, db.Set<Actor>()));
        if (page.Before is { } before)
        {
            beeps = beeps.Where(b => b.Id < before);
        }

        if (page.SinceId is { } since)
        {
            beeps = beeps.Where(b => b.Id > since);
        }

        return beeps;
    }

    /// <summary>
    /// The actors and every agent of theirs. Agents that were deleted or never connected are in it too; they have
    /// no visible beeps, and leaving them in keeps the cached list unchanged until a new agent is created.
    /// </summary>
    private async Task<List<Guid>> AuthorsAsync(IReadOnlyCollection<Guid> actorIds, CancellationToken cancellationToken)
    {
        var agents = await reads.GetAgentIdsAsync(actorIds, cancellationToken);
        return [.. actorIds.Concat(agents.Values.SelectMany(a => a)).Distinct()];
    }
}
