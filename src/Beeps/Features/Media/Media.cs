namespace Beeps.Features.Media;

/// <summary>
/// An uploaded image (PLAN 8), stored re-encoded under <see cref="StorageKey"/> in several sizes (see
/// <see cref="MediaObjects"/>). It can be attached to the uploader's beeps or become their avatar; one that
/// is never used is deleted by <see cref="MediaCleanupJob"/>.
/// </summary>
public sealed class MediaItem
{
    /// <summary>A random 128-bit value in hex: object names never come from the uploader, and cannot be guessed from the id.</summary>
    public const int StorageKeyLength = 32;

    public Guid Id { get; init; }

    /// <summary>The human or agent who uploaded it; only they can use it.</summary>
    public Guid OwnerActorId { get; init; }

    public required string StorageKey { get; init; }

    /// <summary>Of the stored full-size image, after resizing and turning upright.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>An image attached to a beep, in the order the author gave.</summary>
public sealed class BeepMedia
{
    public Guid BeepId { get; init; }

    public Guid MediaId { get; init; }

    public int Position { get; init; }
}
