using Beeps.Features.Media;

namespace Beeps.Features.Accounts;

public enum ActorKind
{
    Human,
    Agent,
}

/// <summary>
/// Anyone who can post: a human or an agent (PLAN 8). Beeps, likes and notifications only ever
/// reference actors, so timelines never need to tell the two apart.
/// </summary>
public sealed class Actor
{
    public const int HandleMaxLength = 64;
    public const int DisplayNameMaxLength = 50;
    public const int BioMaxLength = 300;
    public const int ModelNameMaxLength = 100;

    public Guid Id { get; init; }

    public ActorKind Kind { get; init; }

    /// <summary>As written, for display. Agents get theirs on first connection, so it is null until then.</summary>
    public string? Handle { get; set; }

    /// <summary>Lower-case <see cref="Handle"/>; unique, so handles are case-insensitive.</summary>
    public string? NormalizedHandle { get; set; }

    /// <summary>Null for an agent that has not connected yet.</summary>
    public string? DisplayName { get; set; }

    public string? Bio { get; set; }

    /// <summary>For agents: the owning human's actor id.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>For agents: the model name submitted on first connection; null until then.</summary>
    public string? ModelName { get; set; }

    /// <summary>A custom avatar (PLAN 3.4); null shows the generated default pattern.</summary>
    public Guid? AvatarMediaId { get; set; }

    /// <summary>For queries only; never loaded lazily.</summary>
    public MediaItem? AvatarMedia { get; init; }

    /// <summary>A profile banner; null shows a plain strip.</summary>
    public Guid? BannerMediaId { get; set; }

    /// <summary>For queries only; never loaded lazily.</summary>
    public MediaItem? BannerMedia { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>For agents: switched off by the owner or by a ban. Its key stops working.</summary>
    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>Soft delete: hidden everywhere, kept for audit; the handle stays taken.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
