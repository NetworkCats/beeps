namespace Beeps.Features.Accounts;

/// <summary>
/// A passkey the human added in the settings: signs in without a password, or, if the account says so
/// (<see cref="User.PasskeyTwoFactor"/>), is the second step after the password or Google and GitHub.
/// </summary>
public sealed class Passkey
{
    /// <summary>WebAuthn allows credential ids up to this long.</summary>
    public const int CredentialIdMaxLength = 1023;

    public const int UserAgentMaxLength = UserSession.UserAgentMaxLength;

    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    /// <summary>The authenticator's id for it, which the browser names it by when signing in.</summary>
    public required byte[] CredentialId { get; init; }

    /// <summary>Its public key, COSE-encoded, as the authenticator gave it.</summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>How many times the authenticator says it has signed; most synced passkeys always say 0.</summary>
    public long SignCount { get; set; }

    /// <summary>How the browser can reach the authenticator (<c>internal</c>, <c>hybrid</c>, <c>usb</c>…), as it said.</summary>
    public string[]? Transports { get; init; }

    /// <summary>It may be copied to the owner's other devices (synced), as the authenticator said when it was made.</summary>
    public bool BackupEligible { get; init; }

    public bool BackedUp { get; set; }

    /// <summary>Where it was added, which names it in the settings (<see cref="UserAgents.Describe"/>).</summary>
    public string? UserAgent { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>What the browser is asked (<c>navigator.credentials</c>'s options, as JSON), and the state its answer is checked against.</summary>
public sealed record PasskeyChallenge(string OptionsJson, string State);

/// <summary>A passkey the authenticator just made, checked: what is kept of it.</summary>
public sealed record NewPasskey(byte[] CredentialId, byte[] PublicKey, uint SignCount, string[]? Transports, bool BackupEligible, bool BackedUp);

/// <summary>A passkey that just signed, checked: what the authenticator now says of it.</summary>
public sealed record PasskeyUse(uint SignCount, bool BackedUp);

/// <summary>What the browser is asked, and the ticket that carries its state back with the answer (<see cref="AccountService"/>).</summary>
public sealed record PasskeyRequest(string OptionsJson, string Ticket);

/// <summary>A passkey as the settings list it.</summary>
public sealed record PasskeySummary(Guid Id, DeviceInfo AddedOn, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
