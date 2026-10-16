namespace Beeps.Features.Accounts;

/// <summary>A single-use registration code for invite-only mode. Only the hash is stored.</summary>
public sealed class Invite
{
    public Guid Id { get; init; }

    public required string CodeHash { get; init; }

    /// <summary>The first characters, so admins can tell invites apart.</summary>
    public required string CodePrefix { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public Guid? UsedBy { get; init; }

    public DateTimeOffset? UsedAt { get; init; }
}
