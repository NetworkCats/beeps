using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Media;

/// <summary>Outbox payload: decode one accepted upload, store every size and make it <see cref="MediaStatus.Ready"/>.</summary>
public sealed record ProcessMediaRequest(Guid MediaId)
{
    public const string MessageType = "media.process";
}

/// <summary>
/// Image post-processing, off the upload request: decodes the original, turns it upright,
/// re-encodes the sizes its <see cref="MediaPurpose"/> needs as WebP without metadata and stores them. A storage failure throws, so the message is
/// retried with backoff; a file that does not decode can never succeed, so it is marked
/// <see cref="MediaStatus.Failed"/> instead. Idempotent: once processed, the original is gone and a repeat does nothing.
/// Workers handle one message at a time, which also caps how many images an instance decodes at once.
/// </summary>
public sealed partial class ProcessMediaHandler(
    IMediaStore store,
    IImageProcessor images,
    IMediaStorage storage,
    IUnitOfWork unitOfWork,
    IAccountStore accounts,
    BeepService beeps,
    LiveUpdates live,
    ILogger<ProcessMediaHandler> logger) : IOutboxHandler
{
    public string Type => ProcessMediaRequest.MessageType;

    public string Queue => OutboxQueues.Media;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var mediaId = message.ReadPayload(MediaJsonContext.Default.ProcessMediaRequest).MediaId;
        var upload = await store.FindUploadAsync(mediaId, cancellationToken);
        if (upload is null)
        {
            // Processed by an earlier delivery, or deleted as unused.
            return;
        }

        var variants = images.Encode(upload.Data, upload.Purpose, upload.Crop, upload.BannerCrop);
        if (variants is null)
        {
            await FinishAsync(mediaId, MediaStatus.Failed, upload.Width, upload.Height, cancellationToken);
            LogUnreadable(logger, mediaId);
            // Members never saw it; the admin page of the beep shows it as failed now. A beep of nothing else goes.
            var usedBy = await store.ListBeepsUsingAsync(mediaId, cancellationToken);
            await beeps.DeleteEmptyAsync(usedBy, cancellationToken);
            await live.BeepsChangedAsync(usedBy, cancellationToken);
            return;
        }

        // Overwriting the same names is harmless, so a retry after a partial failure simply writes them all again.
        (string Name, byte[]? Content)[] objects =
        [
            (MediaObjects.Full(upload.StorageKey), variants.Full),
            (MediaObjects.Thumbnail(upload.StorageKey), variants.Thumbnail),
            (MediaObjects.Avatar(upload.StorageKey), variants.Avatar),
            (MediaObjects.Banner(upload.StorageKey), variants.Banner),
        ];
        await Task.WhenAll(objects
            .Where(o => o.Content is not null)
            .Select(o => storage.PutAsync(o.Name, o.Content!, MediaObjects.ContentType, cancellationToken)));
        switch (await FinishAsync(mediaId, MediaStatus.Ready, variants.Width, variants.Height, cancellationToken))
        {
            case MediaFinish.AlreadyReady:
                // A second delivery (the lease ran out mid-message) wrote the same bytes over the ones in use.
                return;
            case MediaFinish.Gone:
                // The row went while the objects were written; nothing will ever point at them.
                await storage.DeleteAsync(MediaObjects.All(upload.StorageKey), cancellationToken);
                return;
        }

        LogProcessed(logger, mediaId, variants.Width, variants.Height);

        // A beep posted while it was processing showed without it until now (pages show it as it arrives).
        await live.BeepsChangedAsync(await store.ListBeepsUsingAsync(mediaId, cancellationToken), cancellationToken);

        // An avatar or banner set while it was processing showed the default pattern or no banner until now.
        foreach (var actorId in await store.ListActorsUsingAsync(mediaId, cancellationToken))
        {
            await accounts.ForgetAccountAsync(actorId, cancellationToken);
            await live.ActorChangedAsync(actorId, cancellationToken);
        }
    }

    private async Task<MediaFinish> FinishAsync(Guid mediaId, MediaStatus status, int width, int height, CancellationToken cancellationToken)
    {
        var finished = await unitOfWork.InTransactionAsync<MediaFinish>(
            null,
            async ct => Result.Of(await store.FinishProcessingAsync(mediaId, status, width, height, ct)),
            cancellationToken);
        return finished.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {MediaId} processed ({Width}x{Height})")]
    private static partial void LogProcessed(ILogger logger, Guid mediaId, int width, int height);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Media {MediaId} could not be decoded and will not be shown")]
    private static partial void LogUnreadable(ILogger logger, Guid mediaId);
}
