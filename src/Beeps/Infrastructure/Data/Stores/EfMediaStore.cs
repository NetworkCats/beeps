using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Media;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfMediaStore(BeepsDbContext db, PendingCacheChanges cacheChanges) : IMediaStore, IAvatarStore
{
    public void AddMedia(MediaItem media) => db.Set<MediaItem>().Add(media);

    public void AddUpload(MediaUpload upload) => db.Set<MediaUpload>().Add(upload);

    public Task<MediaUploadJob?> FindUploadAsync(Guid mediaId, CancellationToken cancellationToken) =>
        db.Set<MediaUpload>()
            .AsNoTracking()
            .Where(u => u.MediaId == mediaId)
            .Join(db.Set<MediaItem>(), u => u.MediaId, m => m.Id, (u, m) => new { u, m })
            .Select(x => new MediaUploadJob(
                x.m.StorageKey,
                x.m.Width,
                x.m.Height,
                x.u.Data,
                x.u.CropSize == null ? null : new AvatarCrop(x.u.CropX ?? 0, x.u.CropY ?? 0, x.u.CropSize.Value),
                x.u.BannerCropWidth == null ? null : new BannerCrop(x.u.BannerCropX ?? 0, x.u.BannerCropY ?? 0, x.u.BannerCropWidth.Value)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> FinishProcessingAsync(Guid mediaId, MediaStatus status, int width, int height, CancellationToken cancellationToken)
    {
        var updated = await db.Set<MediaItem>()
            .Where(m => m.Id == mediaId && m.Status == MediaStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status).SetProperty(m => m.Width, width).SetProperty(m => m.Height, height), cancellationToken);
        await db.Set<MediaUpload>().Where(u => u.MediaId == mediaId).ExecuteDeleteAsync(cancellationToken);
        if (updated > 0 && status == MediaStatus.Ready)
        {
            // Beeps, avatars and banners only show ready images, so whatever already uses this one shows it from now on.
            var beeps = await db.Set<BeepMedia>().Where(bm => bm.MediaId == mediaId).Select(bm => bm.BeepId).ToListAsync(cancellationToken);
            var actors = await ListActorsUsingAsync(mediaId, cancellationToken);
            cacheChanges.Add([.. beeps.Select(CacheChange.Beep), .. actors.Select(CacheChange.Actor)]);
        }

        return updated > 0;
    }

    public async Task<IReadOnlyList<Guid>> ListBeepsUsingAsync(Guid mediaId, CancellationToken cancellationToken) =>
        await db.Set<BeepMedia>().Where(m => m.MediaId == mediaId).Select(m => m.BeepId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListActorsUsingAsync(Guid mediaId, CancellationToken cancellationToken) =>
        await db.Set<Actor>().Where(a => a.AvatarMediaId == mediaId || a.BannerMediaId == mediaId).Select(a => a.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> LockOwnedAsync(Guid ownerActorId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken)
    {
        var ids = mediaIds.ToArray();
        string[] usable = [EnumText.ToValue(MediaStatus.Processing), EnumText.ToValue(MediaStatus.Ready)];
        // KEY SHARE: the weakest lock that still blocks a delete, and the one a foreign key check takes anyway.
        // Media still processing can be used; they are shown once ready.
        return await db.Database
            .SqlQuery<Guid>($"""SELECT id AS "Value" FROM media WHERE id = ANY({ids}) AND owner_actor_id = {ownerActorId} AND status = ANY({usable}) FOR KEY SHARE""")
            .ToListAsync(cancellationToken);
    }

    public async Task<UnusedMediaBatch> DeleteUnusedAsync(DateTimeOffset cutoff, MediaPosition? after, int batchSize, CancellationToken cancellationToken)
    {
        // Media are never older than this, so it stands for "from the start".
        var (afterCreatedAt, afterId) = after is null ? (DateTimeOffset.UnixEpoch, Guid.Empty) : (after.CreatedAt, after.Id);
        // SKIP LOCKED passes over media a beep, an avatar or a banner is taking right now (see LockOwnedAsync).
        var deleted = await db.Database
            .SqlQuery<DeletedMedia>(
                $"""
                DELETE FROM media WHERE id IN (
                    SELECT m.id FROM media m
                    WHERE m.created_at < {cutoff}
                      AND m.created_at >= {afterCreatedAt} AND (m.created_at, m.id) > ({afterCreatedAt}, {afterId})
                      AND NOT EXISTS (SELECT 1 FROM beep_media bm WHERE bm.media_id = m.id)
                      AND NOT EXISTS (SELECT 1 FROM actors a WHERE a.avatar_media_id = m.id)
                      AND NOT EXISTS (SELECT 1 FROM actors a WHERE a.banner_media_id = m.id)
                    ORDER BY m.created_at, m.id
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING storage_key, created_at, id
                """)
            .ToListAsync(cancellationToken);
        var last = deleted.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).LastOrDefault();
        return new UnusedMediaBatch([.. deleted.Select(d => d.StorageKey)], last is null ? null : new MediaPosition(last.CreatedAt, last.Id));
    }

    public Task<AvatarTarget?> FindActorAsync(Guid actorId, CancellationToken cancellationToken) =>
        db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.Id == actorId && a.DeletedAt == null)
            .Select(a => new AvatarTarget(a.Id, a.Kind, a.OwnerId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetAvatarAsync(Guid actorId, Guid? mediaId, CancellationToken cancellationToken)
    {
        await db.Set<Actor>()
            .Where(a => a.Id == actorId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AvatarMediaId, mediaId), cancellationToken);
        cacheChanges.Add(CacheChange.Actor(actorId));
    }

    public async Task SetBannerAsync(Guid actorId, Guid? mediaId, CancellationToken cancellationToken)
    {
        await db.Set<Actor>()
            .Where(a => a.Id == actorId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.BannerMediaId, mediaId), cancellationToken);
        cacheChanges.Add(CacheChange.Actor(actorId));
    }

    public Task<string?> FindBannerKeyAsync(Guid actorId, CancellationToken cancellationToken) =>
        db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.Id == actorId && a.BannerMedia != null && a.BannerMedia.Status == MediaStatus.Ready)
            .Select(a => a.BannerMedia!.StorageKey)
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record DeletedMedia(string StorageKey, DateTimeOffset CreatedAt, Guid Id);
}
