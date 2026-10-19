using System.Security.Cryptography;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Media;

/// <summary>
/// Image uploads (PLAN 6.11) for humans and agents. Nothing about the file is trusted: the type comes from
/// its magic number and the size from its header, both checked here before anything is decoded. Decoding and
/// re-encoding as WebP without metadata happen afterwards in <see cref="ProcessMediaHandler"/> (PLAN 6.8), which
/// stores the result under a random name.
/// </summary>
public sealed partial class MediaService(
    IImageProcessor images,
    IMediaStore store,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    MediaUrls urls,
    RateLimitService rateLimits,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<MediaService> logger)
{
    /// <summary>
    /// Accepts one image and queues its processing; its id can be attached to a beep or made the uploader's
    /// avatar right away, and shows once processed.
    /// </summary>
    public Task<Result<MediaView>> UploadAsync(Viewer uploader, MediaContent content, CancellationToken cancellationToken) =>
        UploadAsync(uploader, content, null, false, null, cancellationToken);

    /// <summary>
    /// Accepts one image whose avatar size is <paramref name="avatarCrop"/> (the web avatar forms) instead of
    /// the centered square. A crop outside the image is an error.
    /// </summary>
    public Task<Result<MediaView>> UploadAsync(Viewer uploader, MediaContent content, AvatarCrop? avatarCrop, CancellationToken cancellationToken) =>
        UploadAsync(uploader, content, avatarCrop, false, null, cancellationToken);

    /// <summary>
    /// Accepts one image from a banner form: only these get a banner size, which is <paramref name="bannerCrop"/>, or
    /// the centered strip without one. A crop outside the image is an error.
    /// </summary>
    public Task<Result<MediaView>> UploadBannerAsync(Viewer uploader, MediaContent content, BannerCrop? bannerCrop, CancellationToken cancellationToken) =>
        UploadAsync(uploader, content, null, true, bannerCrop, cancellationToken);

    private async Task<Result<MediaView>> UploadAsync(
        Viewer uploader, MediaContent content, AvatarCrop? avatarCrop, bool asBanner, BannerCrop? bannerCrop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploader);
        ArgumentNullException.ThrowIfNull(content);
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.MediaUpload, RateLimitPartitions.Actor(uploader.ActorId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        var data = await ReadAsync(content, cancellationToken);
        if (data is null)
        {
            return Invalid(MediaErrorCodes.TooLarge, localizer["Error.MediaTooLarge", MediaRules.MaxBytes / (1024 * 1024)], ErrorKind.TooLarge);
        }

        var header = Check(data);
        if (!header.IsSuccess)
        {
            return header.Error;
        }

        AvatarCrop? crop = null;
        if (avatarCrop is not null && (crop = MediaRules.FitCrop(avatarCrop, header.Value.Width, header.Value.Height)) is null)
        {
            return Invalid(MediaErrorCodes.AvatarCropInvalid, localizer["Error.AvatarCropInvalid"]);
        }

        BannerCrop? banner = null;
        if (asBanner)
        {
            banner = bannerCrop is null
                ? MediaRules.CenteredBanner(header.Value.Width, header.Value.Height)
                : MediaRules.FitBannerCrop(bannerCrop, header.Value.Width, header.Value.Height);
            if (banner is null)
            {
                return Invalid(MediaErrorCodes.BannerCropInvalid, localizer["Error.BannerCropInvalid"]);
            }
        }

        return await QueueAsync(uploader, data, header.Value, crop, banner, cancellationToken);
    }

    /// <summary>
    /// The images of a beep written on the web, uploaded together; at most <see cref="MediaRules.MaxImagesPerBeep"/>.
    /// Stops at the first that fails: the ones already accepted are never used, so the cleanup job removes them.
    /// </summary>
    public async Task<Result<IReadOnlyList<Guid>>> UploadAllAsync(Viewer uploader, IReadOnlyList<MediaContent> contents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (contents.Count > MediaRules.MaxImagesPerBeep)
        {
            return TooMany(MediaFields.Images);
        }

        var ids = new List<Guid>(contents.Count);
        foreach (var content in contents)
        {
            var uploaded = await UploadAsync(uploader, content, cancellationToken);
            if (!uploaded.IsSuccess)
            {
                return uploaded.Error;
            }

            ids.Add(uploaded.Value.Id);
        }

        return ids;
    }

    /// <summary>The error for more images than a beep can have; the field is the API's or the web form's.</summary>
    public Error TooMany(string field) => Error.Field(MediaErrorCodes.TooMany, field, localizer["Error.TooManyMedia", MediaRules.MaxImagesPerBeep]);

    /// <summary>The error for media ids that are unknown or were uploaded by someone else: both look the same.</summary>
    public Error NotFound(string field) => Error.Field(MediaErrorCodes.NotFound, field, localizer["Error.MediaNotFound"]);

    /// <summary>The error for an API upload sent as a form instead of the raw image bytes.</summary>
    public Error RawBodyRequired() => Error.Validation(MediaErrorCodes.RawBodyRequired, localizer["Error.MediaRawBodyRequired"]);

    /// <summary>Reads at most one byte past the limit, so an oversized upload is never held in full; null if too large.</summary>
    private static async Task<byte[]?> ReadAsync(MediaContent content, CancellationToken cancellationToken)
    {
        if (content.Length > MediaRules.MaxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream(content.Length is { } length and > 0 ? (int)length : 64 * 1024);
        var chunk = new byte[81920];
        int read;
        while ((read = await content.Stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MediaRules.MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private Result<ImageHeader> Check(byte[] data)
    {
        var format = MediaRules.Sniff(data);
        if (format is null)
        {
            return Invalid(MediaErrorCodes.TypeUnsupported, localizer["Error.MediaTypeUnsupported"]);
        }

        // A header that disagrees with the magic number is a disguised file.
        var header = images.ReadHeader(data);
        if (header is null || header.Format != format)
        {
            return Invalid(MediaErrorCodes.Unreadable, localizer["Error.MediaUnreadable"]);
        }

        return MediaRules.CheckSize(header.Width, header.Height) switch
        {
            MediaSizeProblem.TooSmall => Invalid(MediaErrorCodes.DimensionsTooSmall, localizer["Error.MediaTooSmall", MediaRules.MinSide]),
            MediaSizeProblem.TooLarge => Invalid(MediaErrorCodes.DimensionsTooLarge, localizer["Error.MediaDimensionsTooLarge", MediaRules.MaxSide, Math.Round(MediaRules.MaxPixels / 1_000_000d, 1)]),
            _ => header,
        };
    }

    /// <summary>
    /// The media row (with the size the stored image will have), the original file and the message that
    /// processes it, all in one transaction: nothing is left half-done, and the storage is not touched here.
    /// </summary>
    private async Task<Result<MediaView>> QueueAsync(
        Viewer uploader, byte[] data, ImageHeader header, AvatarCrop? crop, BannerCrop? banner, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (width, height) = MediaRules.FitWithin(header.Width, header.Height, MediaRules.FullMaxSide);
        var media = new MediaItem
        {
            Id = Guid.CreateVersion7(now),
            OwnerActorId = uploader.ActorId,
            StorageKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(MediaItem.StorageKeyLength / 2)),
            Width = width,
            Height = height,
            Status = MediaStatus.Processing,
            CreatedAt = now,
        };
        store.AddMedia(media);
        store.AddUpload(new MediaUpload
        {
            MediaId = media.Id,
            Data = data,
            CropX = crop?.X,
            CropY = crop?.Y,
            CropSize = crop?.Size,
            BannerCropX = banner?.X,
            BannerCropY = banner?.Y,
            BannerCropWidth = banner?.Width,
        });
        outbox.Enqueue(ProcessMediaRequest.MessageType, new ProcessMediaRequest(media.Id), MediaJsonContext.Default.ProcessMediaRequest);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogUploaded(logger, media.Id, uploader.ActorId, header.Width, header.Height);
        return urls.View(new MediaRow(media.Id, media.StorageKey, media.Width, media.Height, media.Status));
    }

    private static Error Invalid(string code, string detail, ErrorKind kind = ErrorKind.Validation) => new(kind, code, detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {MediaId} uploaded by {ActorId} ({Width}x{Height}), queued for processing")]
    private static partial void LogUploaded(ILogger logger, Guid mediaId, Guid actorId, int width, int height);
}
