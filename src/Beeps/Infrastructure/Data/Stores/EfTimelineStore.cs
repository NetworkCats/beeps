using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>Read-time timelines: the author set is a subquery, the beeps come off the <c>(author_id, id DESC)</c> index.</summary>
public sealed class EfTimelineStore(BeepsDbContext db) : ITimelineStore
{
    public Task<CursorPage<BeepRow>> PublicAsync(PageRequest page, Guid? viewerId, CancellationToken cancellationToken) =>
        BeepQueries.Visible(db, viewerId)
            .Where(b => b.ReplyToId == null)
            .NewestFirstAsync(db, page, viewerId, cancellationToken);

    public Task<CursorPage<BeepRow>> HomeAsync(Guid humanId, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        // Following a human means following all of their agents too (PLAN 6.6).
        var followed = db.Set<Follow>().Where(f => f.FollowerId == humanId).Select(f => f.FolloweeId);
        var authors = db.Set<Actor>()
            .Where(a => a.Id == humanId
                || a.OwnerId == humanId
                || followed.Contains(a.Id)
                || (a.OwnerId != null && followed.Contains(a.OwnerId.Value)))
            .Select(a => a.Id);
        // A reply belongs here only when it answers a visible beep by one of the same authors (PLAN 6.6).
        var answerable = BeepQueries.Visible(db, viewerId).Where(p => authors.Contains(p.AuthorId)).Select(p => p.Id);
        return BeepQueries.Visible(db, viewerId)
            .Where(b => authors.Contains(b.AuthorId) && (b.ReplyToId == null || answerable.Contains(b.ReplyToId.Value)))
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

    public Task<CursorPage<BeepRow>> HashtagAsync(string normalizedTag, PageRequest page, Guid? viewerId, CancellationToken cancellationToken)
    {
        var tagId = db.Set<Hashtag>().Where(h => h.Name == normalizedTag).Select(h => h.Id);
        var tagged = db.Set<BeepHashtag>().Where(t => tagId.Contains(t.HashtagId)).Select(t => t.BeepId);
        return BeepQueries.Visible(db, viewerId)
            .Where(b => tagged.Contains(b.Id))
            .NewestFirstAsync(db, page, viewerId, cancellationToken);
    }
}
