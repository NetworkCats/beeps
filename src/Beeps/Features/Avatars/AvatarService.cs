using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Avatars;

/// <summary>An actor whose avatar or banner can change.</summary>
public sealed record AvatarTarget(Guid Id, ActorKind Kind, Guid? OwnerId);

public interface IAvatarStore
{
    /// <summary>An actor that is not deleted.</summary>
    Task<AvatarTarget?> FindActorAsync(Guid actorId, CancellationToken cancellationToken);

    /// <summary>Sets or (with null) removes the custom avatar. Runs immediately.</summary>
    Task SetAvatarAsync(Guid actorId, Guid? mediaId, CancellationToken cancellationToken);

    /// <summary>Sets or (with null) removes the profile banner. Runs immediately.</summary>
    Task SetBannerAsync(Guid actorId, Guid? mediaId, CancellationToken cancellationToken);

    /// <summary>The storage key of the actor's banner once it is ready; null for none.</summary>
    Task<string?> FindBannerKeyAsync(Guid actorId, CancellationToken cancellationToken);
}

/// <summary>
/// Custom avatars: an image uploaded as an avatar becomes the viewer's, or one of their agents' if the
/// viewer is a human; an agent only ever changes its own. Each change is a new upload: beep images cannot be used.
/// Removing it brings the default pattern back.
/// </summary>
public sealed partial class AvatarService(
    IAvatarStore store,
    IMediaStore mediaStore,
    MediaService media,
    IAccountStore accounts,
    IUnitOfWork unitOfWork,
    RateLimitService rateLimits,
    LiveUpdates live,
    IStringLocalizer<SharedResource> localizer,
    ILogger<AvatarService> logger)
{
    /// <summary>
    /// Uploads an image and makes it the avatar of <paramref name="actorId"/>. <paramref name="crop"/> is the square
    /// the person picked in the cropper; without it (no script, and the API) the centered square is used.
    /// </summary>
    public async Task<Result> UploadAsync(Viewer viewer, Guid actorId, MediaContent file, AvatarCrop? crop, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(viewer, actorId, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var uploaded = await media.UploadAvatarAsync(viewer, file, crop, cancellationToken);
        return uploaded.IsSuccess ? await ChangeAsync(viewer, target.Value, uploaded.Value, cancellationToken) : uploaded.Error;
    }

    /// <summary>Back to the default pattern. The image itself is deleted later, once nothing uses it.</summary>
    public async Task<Result> RemoveAsync(Viewer viewer, Guid actorId, CancellationToken cancellationToken)
    {
        var target = await CheckAsync(viewer, actorId, cancellationToken);
        return target.IsSuccess ? await ChangeAsync(viewer, target.Value, null, cancellationToken) : target.Error;
    }

    /// <summary>Someone else's actor looks exactly like a missing one. Counts against the avatar quota.</summary>
    private async Task<Result<AvatarTarget>> CheckAsync(Viewer viewer, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var target = await store.FindActorAsync(actorId, cancellationToken);
        if (target is null || !viewer.MayChangeAvatarOf(target.Id, target.OwnerId))
        {
            return Error.NotFound(ErrorCodes.NotFound, localizer["Error.ProfileNotFound"]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AvatarChange, RateLimitPartitions.Actor(viewer.ActorId), cancellationToken);
        return limited.IsSuccess ? target : limited.Error;
    }

    private async Task<Result> ChangeAsync(Viewer viewer, AvatarTarget target, Guid? mediaId, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                // Locked until commit, so the cleanup of unused uploads cannot take it meanwhile. Only just uploaded, it
                // can only be missing if it already failed to decode.
                if (mediaId is { } id && (await mediaStore.LockOwnedAsync(viewer.ActorId, [id], MediaPurpose.Avatar, ct)).Count == 0)
                {
                    return media.Unreadable();
                }

                await store.SetAvatarAsync(target.Id, mediaId, ct);
                return Result.Success;
            },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        // The navigation bar shows a human's avatar from the cached account state.
        if (target.Kind == ActorKind.Human)
        {
            await accounts.ForgetAccountAsync(target.Id, cancellationToken);
        }

        LogChanged(logger, target.Id, viewer.ActorId, mediaId);
        await live.ActorChangedAsync(target.Id, cancellationToken);
        return Result.Success;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Avatar of {ActorId} changed by {ViewerId} to media {MediaId}")]
    private static partial void LogChanged(ILogger logger, Guid actorId, Guid viewerId, Guid? mediaId);
}
