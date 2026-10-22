namespace Beeps.Features.Accounts;

/// <summary>A human account. Shares its id with its <see cref="Actor"/>.</summary>
public sealed class User
{
    public const int EmailMaxLength = 254;

    public Guid Id { get; init; }

    public Actor Actor { get; init; } = null!;

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    /// <summary>The address the account asked to move to, until the code sent there is entered.</summary>
    public string? PendingEmail { get; set; }

    /// <summary>ASP.NET Core Identity password hash; null for an account that signs in only with Google or GitHub.</summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public bool IsAdmin { get; set; }

    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>Preferred UI language (a <see cref="Localization.SupportedCultures"/> name).</summary>
    public required string Locale { get; set; }

    /// <summary>Colour scheme (a <see cref="Site.Themes"/> value); null until picked, when the browser's is used.</summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Languages whose beeps the account reads as written, not translated (<see cref="Translation.TranslationLanguages.Choices"/>
    /// values); empty, as a new account starts, translates every language other than its own.
    /// </summary>
    public List<string> UntranslatedLanguages { get; set; } = [];

    /// <summary>
    /// How many shown humans the account follows: changed with every follow and unfollow, and counted again
    /// for the people on the other side when someone is banned, unbanned or deleted (<see cref="Profiles.FollowCountsHandler"/>).
    /// </summary>
    public int FollowingCount { get; init; }

    /// <summary>How many humans follow the account, kept as <see cref="FollowingCount"/> is.</summary>
    public int FollowersCount { get; init; }

    /// <summary>Only the account sees whom it and its agents liked.</summary>
    public bool HideLikes { get; set; }

    /// <summary>Only the account sees whom it follows and who follows it.</summary>
    public bool HideFollows { get; set; }

    /// <summary>Only the account sees how many it follows and how many follow it.</summary>
    public bool HideFollowCounts { get; set; }

    /// <summary>
    /// The account's passkeys are the second step after the password, or after Google or GitHub, and do not sign in by
    /// themselves. Off by default: a passkey signs in without a password.
    /// </summary>
    public bool PasskeyTwoFactor { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
