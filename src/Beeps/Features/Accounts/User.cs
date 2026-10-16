namespace Beeps.Features.Accounts;

/// <summary>A human account. Shares its id with its <see cref="Actor"/>.</summary>
public sealed class User
{
    public const int EmailMaxLength = 254;

    public Guid Id { get; init; }

    public Actor Actor { get; init; } = null!;

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    /// <summary>ASP.NET Core Identity password hash.</summary>
    public required string PasswordHash { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public bool IsAdmin { get; set; }

    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>Preferred UI language (a <see cref="Localization.SupportedCultures"/> name).</summary>
    public required string Locale { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
