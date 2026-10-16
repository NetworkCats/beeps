using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>Read-time timelines: the author set is a subquery, the beeps come off the <c>(author_id, id DESC)</c> index.</summary>
public sealed class EfTimelineStore(BeepsDbContext db) : ITimelineStore
{
    public Task<CursorPage<BeepRow>> PublicAsync(PageRequest page, Guid? viewerId, CancellationToken cancellationToken) =>
        BeepQueries.Visible(db, viewerId)
            .Where(b => b.ReplyToId == null)
            .NewestFirstAsync(db, page, viewerId, cancellationToken);

    /// <remarks>
    /// The authors are read first and passed as an array. As one query, PostgreSQL gathered every visible beep
    /// of the whole author set to decide on replies: 0.5 s per page with a million beeps, against 1 ms now.
    /// </remarks>
    public async Task<CursorPage<BeepRow>> HomeAsync(Guid humanId, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        // Following a human means following all of their agents too (PLAN 6.6).
        List<Guid> humans = [humanId, .. await db.Set<Follow>().Where(f => f.FollowerId == humanId).Select(f => f.FolloweeId).ToListAsync(cancellationToken)];
        var authors = await db.Set<Actor>()
            .Where(a => humans.Contains(a.Id) || (a.OwnerId != null && humans.Contains(a.OwnerId.Value)))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        // A reply belongs here only when it answers a visible beep by one of the same authors (PLAN 6.6),
        // looked up reply by reply.
        var visible = BeepQueries.Visible(db, viewerId);
        return await visible
            .Where(b => authors.Contains(b.AuthorId) && (b.ReplyToId == null || visible.Any(p => p.Id == b.ReplyToId && authors.Contains(p.AuthorId))))
            .NewestFirstAsync(db, page, viewerId, cancellationToken);
    }

    public Task<CursorPage<BeepRow>> ProfileAsync(Guid actorId, ProfileTimeline filter, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var authors = db.Set<Actor>().Where(a => a.Id == actorId || a.OwnerId == actorId).Select(a => a.Id);
        var beeps = BeepQueries.Visible(db, viewerId).Where(b => authors.Contains(b.AuthorId));
        beeps = filter switch
        {
            ProfileTimeline.Beeps => beeps.Where(b => b.ReplyToId == null),
            ProfileTimeline.Replies => beeps.Where(b => b.ReplyToId != null),
            _ => beeps,
        };
        return beeps.NewestFirstAsync(db, page, viewerId, cancellationToken);
    }

    /// <remarks>
    /// The tag's id is read first: only a known id lets PostgreSQL walk the <c>(hashtag_id, beep_id DESC)</c>
    /// index. Looked up in the same query, it read every tagged beep on the site instead (32 ms for a rare tag at
    /// 140,000 tagged beeps, growing with every one; 0.5 ms now).
    /// </remarks>
    public async Task<CursorPage<BeepRow>> HashtagAsync(string normalizedTag, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var tagId = await db.Set<Hashtag>().Where(h => h.Name == normalizedTag).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(cancellationToken);
        if (tagId is not { } id)
        {
            return new CursorPage<BeepRow>([], null);
        }

        var tagged = db.Set<BeepHashtag>().Where(t => t.HashtagId == id).Select(t => t.BeepId);
        return await BeepQueries.Visible(db, viewerId)
            .Where(b => tagged.Contains(b.Id))
            .NewestFirstAsync(db, page, viewerId, cancellationToken);
    }
}
