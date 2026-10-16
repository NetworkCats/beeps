using System.Security.Cryptography;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Media;

/// <summary>
/// Image uploads (PLAN 6.11) for humans and agents. Nothing about the file is trusted: the type comes from
/// its magic number, the size from its header (checked before decoding), and what is stored is a fresh
/// WebP encoding without metadata, under a random name.
/// </summary>
public sealed partial class MediaService(
    IImageProcessor images,
    IMediaStorage storage,
    IMediaStore store,
    IUnitOfWork unitOfWork,
    MediaUrls urls,
    RateLimitService rateLimits,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<MediaService> logger)
{
    /// <summary>Uploads one image; its id can then be attached to a beep or made the uploader's avatar.</summary>
    public Task<Result<MediaView>> UploadAsync(Viewer uploader, MediaContent content, CancellationToken cancellationToken) =>
        UploadAsync(uploader, content, null, cancellationToken);

    /// <summary>
    /// Uploads one image whose avatar size is <paramref name="avatarCrop"/> (the web avatar forms) instead of
    /// the centered square. A crop outside the image is an error.
    /// </summary>
    public async Task<Result<MediaView>> UploadAsync(Viewer uploader, MediaContent content, AvatarCrop? avatarCrop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploader);
        ArgumentNullException.ThrowIfNull(content);
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.MediaUpload, $"actor:{uploader.ActorId}", cancellationToken);
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

        var variants = images.Encode(data, crop);
        if (variants is null)
        {
            return Invalid(MediaErrorCodes.Unreadable, localizer["Error.MediaUnreadable"]);
        }

        return await StoreAsync(uploader, variants, cancellationToken);
    }

    /// <summary>
    /// The images of a beep written on the web, uploaded together; at most <see cref="MediaRules.MaxImagesPerBeep"/>.
    /// Stops at the first that fails: the ones already stored are never used, so the cleanup job removes them.
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
    public Error TooMany(string field)
    {
        string message = localizer["Error.TooManyMedia", MediaRules.MaxImagesPerBeep];
        return Error.Validation(MediaErrorCodes.TooMany, message, new Dictionary<string, string[]> { [field] = [message] });
    }

    /// <summary>The error for media ids that are unknown or were uploaded by someone else: both look the same.</summary>
    public Error NotFound(string field)
    {
        string message = localizer["Error.MediaNotFound"];
        return Error.Validation(MediaErrorCodes.NotFound, message, new Dictionary<string, string[]> { [field] = [message] });
    }

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

    private async Task<Result<MediaView>> StoreAsync(Viewer uploader, ImageVariants variants, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var media = new MediaItem
        {
            Id = Guid.CreateVersion7(now),
            OwnerActorId = uploader.ActorId,
            StorageKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(MediaItem.StorageKeyLength / 2)),
            Width = variants.Width,
            Height = variants.Height,
            CreatedAt = now,
        };

        // Objects first: a row must never point at objects that are not there. If the row then fails, the
        // objects are removed again; at worst a few unreachable objects stay behind under a random name.
        await Task.WhenAll(
            storage.PutAsync(MediaObjects.Full(media.StorageKey), variants.Full, MediaObjects.ContentType, cancellationToken),
            storage.PutAsync(MediaObjects.Thumbnail(media.StorageKey), variants.Thumbnail, MediaObjects.ContentType, cancellationToken),
            storage.PutAsync(MediaObjects.Avatar(media.StorageKey), variants.Avatar, MediaObjects.ContentType, cancellationToken));
        try
        {
            store.AddMedia(media);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await storage.DeleteAsync(MediaObjects.All(media.StorageKey), CancellationToken.None);
            throw;
        }

        LogUploaded(logger, media.Id, uploader.ActorId, media.Width, media.Height);
        return urls.View(new MediaRow(media.Id, media.StorageKey, media.Width, media.Height));
    }

    private static Error Invalid(string code, string detail, ErrorKind kind = ErrorKind.Validation) => new(kind, code, detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Media {MediaId} uploaded by {ActorId} ({Width}x{Height})")]
    private static partial void LogUploaded(ILogger logger, Guid mediaId, Guid actorId, int width, int height);
}
