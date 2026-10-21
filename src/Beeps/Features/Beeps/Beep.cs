using Beeps.Features.Accounts;

namespace Beeps.Features.Beeps;

/// <summary>Beeps are visible as soon as they are posted and reviewed afterwards.</summary>
public enum ModerationStatus
{
    Pending,
    Allowed,
    Hidden,
    Failed,
    HumanAllowed,
}

/// <summary>A post or a reply. Every beep of a thread shares <see cref="RootId"/>; a top-level beep is its own root.</summary>
public sealed class Beep
{
    public Guid Id { get; init; }

    public Guid AuthorId { get; init; }

    /// <summary>For queries only; never loaded lazily.</summary>
    public Actor Author { get; init; } = null!;

    /// <summary>As written, after <see cref="BeepText.Normalize"/>.</summary>
    public required string Text { get; init; }

    public Guid? ReplyToId { get; init; }

    public Guid RootId { get; init; }

    public ModerationStatus ModerationStatus { get; set; }

    /// <summary>Written back from the live Valkey counters by <see cref="BeepCounterFlushJob"/>; recounted from <see cref="Like"/> rows.</summary>
    public int LikeCount { get; set; }

    /// <summary>Direct replies that are neither deleted nor hidden; written back like <see cref="LikeCount"/>.</summary>
    public int ReplyCount { get; set; }

    /// <summary>Views by signed-in people (<see cref="ViewedBeep"/>), counted as they happen; shown to no one.</summary>
    public long ViewCount { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Soft delete: invisible to everyone, kept for audit.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// A beep a signed-in person opened (its page), and how often: their viewing history, shown to no one. Opening it again
/// soon after (<see cref="BeepService.RepeatViewWindow"/>) is the same view.
/// </summary>
public sealed class ViewedBeep
{
    public Guid UserId { get; init; }

    public Guid BeepId { get; init; }

    public DateTimeOffset FirstViewedAt { get; init; }

    /// <summary>When the last view that counted began.</summary>
    public DateTimeOffset LastViewedAt { get; set; }

    public int ViewCount { get; set; }
}

public sealed class Like
{
    public Guid BeepId { get; init; }

    public Guid ActorId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>An @mention that resolved to an actor when the beep was posted.</summary>
public sealed class Mention
{
    public Guid BeepId { get; init; }

    public Guid ActorId { get; init; }
}

public sealed class Hashtag
{
    public Guid Id { get; init; }

    /// <summary>Normalized (<see cref="BeepText.NormalizeHashtag"/>); unique.</summary>
    public required string Name { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class BeepHashtag
{
    public Guid BeepId { get; init; }

    public Guid HashtagId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
