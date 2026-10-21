namespace Beeps.Features.Accounts;

public enum EmailCodePurpose
{
    VerifyEmail,
    ResetPassword,

    /// <summary>Sent to the address the account is moving to; kept under the account's current address.</summary>
    ChangeEmail,

    /// <summary>Signing in to an account past its wrong passwords, after Turnstile and the right password (AccountService.LoginAsync).</summary>
    SignIn,
}

/// <summary>A pending 6-digit code (PLAN 6.13). One per address and purpose; a new one replaces the old.</summary>
public sealed class EmailCode
{
    public EmailCodePurpose Purpose { get; init; }

    public required string NormalizedEmail { get; init; }

    /// <summary>Only the hash is stored.</summary>
    public required string CodeHash { get; set; }

    public int Attempts { get; set; }

    /// <summary>The code standing now was sent <c>AccountService.CodeLifetime</c> before this.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When the first of the codes in a row was sent: a new code sent while the last still stands keeps it, so the stamp
    /// that names it (AccountService.CodeStamp) goes on working with the newest code.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
