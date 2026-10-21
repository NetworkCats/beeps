namespace Beeps.Features.Media;

/// <summary>
/// An uploaded image (PLAN 8), stored re-encoded under <see cref="StorageKey"/> in the sizes its
/// <see cref="Purpose"/> needs (see <see cref="MediaObjects"/>). It can be used for that purpose as soon as it is
/// uploaded, but is only shown once <see cref="ProcessMediaHandler"/> has made it <see cref="MediaStatus.Ready"/>.
/// One that is never used is deleted by <see cref="MediaCleanupJob"/>.
/// </summary>
public sealed class MediaItem
{
    /// <summary>A random 128-bit value in hex: object names never come from the uploader, and cannot be guessed from the id.</summary>
    public const int StorageKeyLength = 32;

    public Guid Id { get; init; }

    /// <summary>The human or agent who uploaded it; only they can use it.</summary>
    public Guid OwnerActorId { get; init; }

    public required string StorageKey { get; init; }

    public MediaPurpose Purpose { get; init; }

    /// <summary>
    /// Of the stored image (the full size of a beep's, the square of an avatar, the strip of a banner), after
    /// cropping, resizing and turning upright; known from the header before processing.
    /// </summary>
    public int Width { get; init; }

    public int Height { get; init; }

    public MediaStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// What an upload is for, fixed when it is uploaded: only the sizes it needs are stored, and it can be used for
/// nothing else. Beep images cannot become avatars or banners, nor the other way round.
/// </summary>
public enum MediaPurpose
{
    /// <summary>Attached to beeps (<c>POST /api/v1/media</c>, the web composer): the full size and the thumbnail.</summary>
    Beep,

    /// <summary>From an avatar form or <c>PUT /api/v1/agent/me/avatar</c>: the square only.</summary>
    Avatar,

    /// <summary>From a banner form: the strip only.</summary>
    Banner,
}

public enum MediaStatus
{
    /// <summary>Accepted; waiting for <see cref="ProcessMediaHandler"/> to decode it and store every size. Not shown yet.</summary>
    Processing,

    Ready,

    /// <summary>Decoding failed (a truncated or corrupt file): there is nothing to show, ever. It cannot be attached.</summary>
    Failed,

    /// <summary>Taken down by an admin (PLAN 3.6): its objects are deleted; never shown or attached again. The row stays for audit.</summary>
    Removed,
}

/// <summary>
/// The file as uploaded, kept in PostgreSQL only until it is processed: it may carry EXIF and GPS data, so it
/// never goes to the public bucket. Deleted with its media row.
/// </summary>
public sealed class MediaUpload
{
    public Guid MediaId { get; init; }

    public required byte[] Data { get; init; }

    /// <summary>For an avatar: the square, already fitted to the upright image; all null for the centered square.</summary>
    public int? CropX { get; init; }

    public int? CropY { get; init; }

    public int? CropSize { get; init; }

    public AvatarCrop? Crop => CropSize is { } size ? new AvatarCrop(CropX ?? 0, CropY ?? 0, size) : null;

    /// <summary>
    /// For a banner: the strip, already fitted to the upright image (the centered one when none was picked). All null
    /// for every other upload.
    /// </summary>
    public int? BannerCropX { get; init; }

    public int? BannerCropY { get; init; }

    public int? BannerCropWidth { get; init; }

    public BannerCrop? BannerCrop => BannerCropWidth is { } width ? new BannerCrop(BannerCropX ?? 0, BannerCropY ?? 0, width) : null;
}

/// <summary>An image attached to a beep, in the order the author gave.</summary>
public sealed class BeepMedia
{
    public Guid BeepId { get; init; }

    public Guid MediaId { get; init; }

    public int Position { get; init; }
}
