using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Avatars;

/// <summary>
/// Profile banners: the wide picture above a profile, three times as wide as it is high. Only humans set them, on the
/// web: their own, and their agents'. Agents cannot, so only images uploaded from a banner form are stored in a banner
/// size (<see cref="MediaService.UploadBannerAsync"/>). Without one the profile shows a plain strip.
/// </summary>
public sealed partial class BannerService(
    IAvatarStore store,
    IMediaStore mediaStore,
    MediaService media,
    MediaUrls urls,
    IUnitOfWork unitOfWork,
    RateLimitService rateLimits,
    LiveUpdates live,
    IStringLocalizer<SharedResource> localizer,
    ILogger<BannerService> logger)
{
    /// <summary>The actor's banner on the media domain once it is ready; null for none, and while it is processing.</summary>
    public async Task<Uri?> GetUrlAsync(Guid actorId, CancellationToken cancellationToken) =>
        await store.FindBannerKeyAsync(actorId, cancellationToken) is { } key ? urls.Banner(key) : null;

    /// <summary>
    /// Uploads an image and makes it the banner. <paramref name="crop"/> is the strip the person picked in the cropper;
    /// without it (no script) the centered strip is used.
    /// </summary>
    public async Task<Result> UploadAsync(Viewer viewer, Guid actorId, MediaContent file, BannerCrop? crop, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(viewer, actorId, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var uploaded = await media.UploadBannerAsync(viewer, file, crop, cancellationToken);
        return uploaded.IsSuccess ? await ChangeAsync(viewer, target.Value, uploaded.Value.Id, cancellationToken) : uploaded.Error;
    }

    /// <summary>Back to the plain strip. The image itself is deleted later, once nothing uses it.</summary>
    public async Task<Result> RemoveAsync(Viewer viewer, Guid actorId, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(viewer, actorId, cancellationToken);
        return target.IsSuccess ? await ChangeAsync(viewer, target.Value, null, cancellationToken) : target.Error;
    }

    /// <summary>Someone else's actor looks exactly like a missing one, and so does anything to an agent. Counts against the banner quota.</summary>
    private async Task<Result<AvatarTarget>> CheckAsync(Viewer viewer, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var target = viewer.IsAgent ? null : await store.FindActorAsync(actorId, cancellationToken);
        if (target is null || !viewer.MayChangeAvatarOf(target.Id, target.OwnerId))
        {
            return Error.NotFound(ErrorCodes.NotFound, localizer["Error.ProfileNotFound"]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.BannerChange, $"actor:{viewer.ActorId}", cancellationToken);
        return limited.IsSuccess ? target : limited.Error;
    }

    private async Task<Result> ChangeAsync(Viewer viewer, AvatarTarget target, Guid? mediaId, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                // Locked until commit, so the cleanup of unused uploads cannot take it meanwhile.
                if (mediaId is { } id && (await mediaStore.LockOwnedAsync(viewer.ActorId, [id], ct)).Count == 0)
                {
                    return media.NotFound(MediaFields.MediaId);
                }

                await store.SetBannerAsync(target.Id, mediaId, ct);
                return Result.Success;
            },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        LogChanged(logger, target.Id, viewer.ActorId, mediaId);
        await live.ActorChangedAsync(target.Id, cancellationToken);
        return Result.Success;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Banner of {ActorId} changed by {ViewerId} to media {MediaId}")]
    private static partial void LogChanged(ILogger logger, Guid actorId, Guid viewerId, Guid? mediaId);
}
