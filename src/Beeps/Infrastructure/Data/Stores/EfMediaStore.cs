using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfMediaStore(BeepsDbContext db) : IMediaStore, IAvatarStore
{
    public void AddMedia(MediaItem media) => db.Set<MediaItem>().Add(media);

    public async Task<IReadOnlyList<Guid>> LockOwnedAsync(Guid ownerActorId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken)
    {
        var ids = mediaIds.ToArray();
        // KEY SHARE: the weakest lock that still blocks a delete, and the one a foreign key check takes anyway.
        return await db.Database
            .SqlQuery<Guid>($"""SELECT id AS "Value" FROM media WHERE id = ANY({ids}) AND owner_actor_id = {ownerActorId} FOR KEY SHARE""")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> DeleteUnusedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        // SKIP LOCKED passes over media a beep or an avatar is taking right now (see LockOwnedAsync).
        await db.Database
            .SqlQuery<string>(
                $"""
                DELETE FROM media WHERE id IN (
                    SELECT m.id FROM media m
                    WHERE m.created_at < {cutoff}
                      AND NOT EXISTS (SELECT 1 FROM beep_media bm WHERE bm.media_id = m.id)
                      AND NOT EXISTS (SELECT 1 FROM actors a WHERE a.avatar_media_id = m.id)
                    ORDER BY m.created_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING storage_key AS "Value"
                """)
            .ToListAsync(cancellationToken);

    public Task<AvatarTarget?> FindActorAsync(Guid actorId, CancellationToken cancellationToken) =>
        db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.Id == actorId && a.DeletedAt == null)
            .Select(a => new AvatarTarget(a.Id, a.Kind, a.OwnerId))
            .FirstOrDefaultAsync(cancellationToken);

    public Task SetAvatarAsync(Guid actorId, Guid? mediaId, CancellationToken cancellationToken) =>
        db.Set<Actor>()
            .Where(a => a.Id == actorId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AvatarMediaId, mediaId), cancellationToken);
}
