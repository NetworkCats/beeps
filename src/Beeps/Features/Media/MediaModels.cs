namespace Beeps.Features.Media;

/// <summary>An image as stores return it.</summary>
public sealed record MediaRow(Guid Id, string StorageKey, int Width, int Height, MediaStatus Status = MediaStatus.Ready);

/// <summary>Where a cleanup pass got to, so the next batch does not read the media in use before it again.</summary>
public sealed record MediaPosition(DateTimeOffset CreatedAt, Guid Id);

/// <param name="Last">The last media deleted; null when none were.</param>
public sealed record UnusedMediaBatch(IReadOnlyList<string> StorageKeys, MediaPosition? Last);

/// <summary>
/// An image as shown: absolute URLs on the media domain, and the size of the full image. While it is
/// <see cref="MediaStatus.Processing"/> (only right after the upload) the URLs do not work yet.
/// </summary>
public sealed record MediaView(Guid Id, Uri Url, Uri ThumbnailUrl, int Width, int Height, MediaStatus Status = MediaStatus.Ready);

/// <summary>What <see cref="ProcessMediaHandler"/> needs of an upload that is still waiting.</summary>
public sealed record MediaUploadJob(string StorageKey, int Width, int Height, byte[] Data, AvatarCrop? Crop, BannerCrop? BannerCrop = null);

/// <summary>One uploaded file as it arrives; <see cref="Length"/> is what the client declared, if anything.</summary>
public sealed record MediaContent(Stream Stream, long? Length);

/// <summary>
/// Format and size read from an image's header, before any pixel is decoded. The size is upright: as the
/// EXIF orientation says the image is meant to be seen, which is also how browsers show it.
/// </summary>
public sealed record ImageHeader(ImageFormat Format, int Width, int Height);

/// <summary>
/// The square of an image that becomes an avatar, in upright pixels of the uploaded image: what the
/// cropper on the web pages reports. The browser only chooses it; the server does the cropping.
/// </summary>
public sealed record AvatarCrop(int X, int Y, int Size);

/// <summary>
/// The strip of an image that becomes a profile banner, in upright pixels of the uploaded image, as the banner
/// cropper reports it. Its height is always <see cref="MediaRules.BannerAspect"/> times shorter than its width.
/// </summary>
public sealed record BannerCrop(int X, int Y, int Width)
{
    public int Height => Width / MediaRules.BannerAspect;
}

/// <summary>An image decoded and re-encoded as WebP, upright, with no metadata, in every stored size.</summary>
/// <param name="Banner">Only for an image uploaded as a profile banner.</param>
public sealed record ImageVariants(byte[] Full, int Width, int Height, byte[] Thumbnail, byte[] Avatar, byte[]? Banner = null);

/// <summary>Stable error codes for uploads and their use. Published codes must never change.</summary>
public static class MediaErrorCodes
{
    public const string TooLarge = "media_too_large";
    public const string TypeUnsupported = "media_type_unsupported";
    public const string Unreadable = "media_unreadable";
    public const string DimensionsTooSmall = "media_dimensions_too_small";
    public const string DimensionsTooLarge = "media_dimensions_too_large";
    public const string TooMany = "too_many_media";
    public const string NotFound = "media_not_found";
    public const string RawBodyRequired = "media_raw_body_required";
    public const string AvatarCropInvalid = "avatar_crop_invalid";
    public const string BannerCropInvalid = "banner_crop_invalid";
}

/// <summary>Field names in validation errors; the web forms bind their file inputs with the same names.</summary>
public static class MediaFields
{
    /// <summary>The web composer's file input.</summary>
    public const string Images = "Images";

    public const string MediaIds = "media_ids";

    /// <summary>The API's avatar request.</summary>
    public const string MediaId = "media_id";
}

/// <summary>
/// The objects one upload is stored as, under its storage key: names never contain anything the uploader
/// chose. Every object is WebP.
/// </summary>
public static class MediaObjects
{
    public const string ContentType = "image/webp";

    public static string Full(string storageKey) => $"{storageKey}/full.webp";

    public static string Thumbnail(string storageKey) => $"{storageKey}/thumb.webp";

    public static string Avatar(string storageKey) => $"{storageKey}/avatar.webp";

    /// <summary>Only uploads made as a banner have one.</summary>
    public static string Banner(string storageKey) => $"{storageKey}/banner.webp";

    /// <summary>Every object an upload may have; deleting one that was never stored is no error.</summary>
    public static IReadOnlyList<string> All(string storageKey) => [Full(storageKey), Thumbnail(storageKey), Avatar(storageKey), Banner(storageKey)];
}

/// <summary>Image decoding and encoding (PLAN 6.11). Pure computation; the implementation uses SkiaSharp.</summary>
public interface IImageProcessor
{
    /// <summary>Reads the header only; null if it is not an image the processor understands.</summary>
    ImageHeader? ReadHeader(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Decodes the whole image (the first frame of an animation), turns it upright by its EXIF orientation and
    /// re-encodes every size as WebP, which drops all metadata (EXIF, GPS, ICC). The avatar is
    /// <paramref name="avatarCrop"/> (already checked against the upright size), or else the centered square. Only
    /// with <paramref name="bannerCrop"/> (an upload from a banner form) is there a banner size too. Null if decoding fails.
    /// </summary>
    ImageVariants? Encode(ReadOnlyMemory<byte> data, AvatarCrop? avatarCrop = null, BannerCrop? bannerCrop = null);
}

/// <summary>Object storage (PLAN 6.11): standard S3, which is Cloudflare R2 in production and SeaweedFS locally.</summary>
public interface IMediaStorage
{
    /// <summary>Stores a public, immutable object.</summary>
    Task PutAsync(string objectName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes objects; ones that do not exist are no error.</summary>
    Task DeleteAsync(IReadOnlyCollection<string> objectNames, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IMediaStore.FinishProcessingAsync"/> found.</summary>
public enum MediaFinish
{
    Finished,

    /// <summary>An earlier delivery of the same message made it ready: its stored objects are in use.</summary>
    AlreadyReady,

    /// <summary>Deleted, removed or failed meanwhile: nothing shows it, so objects written for it are orphans.</summary>
    Gone,
}

/// <summary>Media data access. Staged writes are committed by <see cref="Shared.IUnitOfWork"/>.</summary>
public interface IMediaStore
{
    void AddMedia(MediaItem media);

    /// <summary>Stages the original file of a media item that is being processed.</summary>
    void AddUpload(MediaUpload upload);

    /// <summary>The original of a media item still being processed; null once processed, or if the item is gone.</summary>
    Task<MediaUploadJob?> FindUploadAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>
    /// Ends processing: sets the status and the size of the stored image and deletes the original. Runs
    /// immediately; call inside a transaction.
    /// </summary>
    Task<MediaFinish> FinishProcessingAsync(Guid mediaId, MediaStatus status, int width, int height, CancellationToken cancellationToken);

    /// <summary>The beeps this media item is attached to.</summary>
    Task<IReadOnlyList<Guid>> ListBeepsUsingAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>The actors whose avatar or banner this media item is.</summary>
    Task<IReadOnlyList<Guid>> ListActorsUsingAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>
    /// Which of these media the actor uploaded (and that neither failed to process nor were removed), locking them (<c>FOR KEY SHARE</c>) until the transaction
    /// ends, so <see cref="DeleteUnusedAsync"/> cannot remove one that is about to be used. Runs immediately;
    /// call inside a transaction.
    /// </summary>
    Task<IReadOnlyList<Guid>> LockOwnedAsync(Guid ownerActorId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> media created before <paramref name="cutoff"/> and after
    /// <paramref name="after"/> (the previous batch's last), oldest first, that no undeleted beep, avatar or banner uses,
    /// skipping any being used right now; a deleted beep's links to them go with them. Runs immediately; call inside a
    /// transaction.
    /// </summary>
    Task<UnusedMediaBatch> DeleteUnusedAsync(DateTimeOffset cutoff, MediaPosition? after, int batchSize, CancellationToken cancellationToken);
}
