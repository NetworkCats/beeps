namespace Beeps.Features.Media;

/// <summary>
/// An uploaded image (PLAN 8), stored re-encoded under <see cref="StorageKey"/> in several sizes (see
/// <see cref="MediaObjects"/>). It can be attached to the uploader's beeps or become their avatar as soon as it
/// is uploaded, but is only shown once <see cref="ProcessMediaHandler"/> has made it <see cref="MediaStatus.Ready"/>.
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

    /// <summary>Of the stored full-size image, after resizing and turning upright; known from the header before processing.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    public MediaStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
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

    /// <summary>The avatar square, already fitted to the upright image; all null for the centered square.</summary>
    public int? CropX { get; init; }

    public int? CropY { get; init; }

    public int? CropSize { get; init; }

    public AvatarCrop? Crop => CropSize is { } size ? new AvatarCrop(CropX ?? 0, CropY ?? 0, size) : null;
}

/// <summary>An image attached to a beep, in the order the author gave.</summary>
public sealed class BeepMedia
{
    public Guid BeepId { get; init; }

    public Guid MediaId { get; init; }

    public int Position { get; init; }
}
