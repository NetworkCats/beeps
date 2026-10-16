namespace Beeps.Features.Media;

/// <summary>An image as stores return it.</summary>
public sealed record MediaRow(Guid Id, string StorageKey, int Width, int Height);

/// <summary>An image as shown: absolute URLs on the media domain, and the size of the full image.</summary>
public sealed record MediaView(Guid Id, Uri Url, Uri ThumbnailUrl, int Width, int Height);

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

/// <summary>An image decoded and re-encoded as WebP, upright, with no metadata, in every stored size.</summary>
public sealed record ImageVariants(byte[] Full, int Width, int Height, byte[] Thumbnail, byte[] Avatar);

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
}

/// <summary>Field names in validation errors; the web forms bind their file inputs with the same names.</summary>
public static class MediaFields
{
    /// <summary>The API's <c>media_ids</c>, and the web composer's file input.</summary>
    public const string Images = "Images";

    public const string MediaIds = "media_ids";

    /// <summary>The API's avatar request.</summary>
    public const string MediaId = "media_id";

    public const string Avatar = "Avatar";
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

    public static IReadOnlyList<string> All(string storageKey) => [Full(storageKey), Thumbnail(storageKey), Avatar(storageKey)];
}

/// <summary>Image decoding and encoding (PLAN 6.11). Pure computation; the implementation uses SkiaSharp.</summary>
public interface IImageProcessor
{
    /// <summary>Reads the header only; null if it is not an image the processor understands.</summary>
    ImageHeader? ReadHeader(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Decodes the whole image (the first frame of an animation), turns it upright by its EXIF orientation and
    /// re-encodes every size as WebP, which drops all metadata (EXIF, GPS, ICC). The avatar is
    /// <paramref name="avatarCrop"/> (already checked against the upright size), or else the centered square.
    /// Null if decoding fails.
    /// </summary>
    ImageVariants? Encode(ReadOnlyMemory<byte> data, AvatarCrop? avatarCrop = null);
}

/// <summary>Object storage (PLAN 6.11): standard S3, which is Cloudflare R2 in production and SeaweedFS locally.</summary>
public interface IMediaStorage
{
    /// <summary>Stores a public, immutable object.</summary>
    Task PutAsync(string objectName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes objects; ones that do not exist are no error.</summary>
    Task DeleteAsync(IReadOnlyCollection<string> objectNames, CancellationToken cancellationToken);
}

/// <summary>Media data access. Staged writes are committed by <see cref="Shared.IUnitOfWork"/>.</summary>
public interface IMediaStore
{
    void AddMedia(MediaItem media);

    /// <summary>
    /// Which of these media the actor uploaded, locking them (<c>FOR KEY SHARE</c>) until the transaction
    /// ends, so <see cref="DeleteUnusedAsync"/> cannot remove one that is about to be used. Runs immediately;
    /// call inside a transaction.
    /// </summary>
    Task<IReadOnlyList<Guid>> LockOwnedAsync(Guid ownerActorId, IReadOnlyCollection<Guid> mediaIds, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> media created before <paramref name="cutoff"/> that no beep
    /// and no avatar uses, skipping any being used right now. Runs immediately; returns their storage keys.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteUnusedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}
