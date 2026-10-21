namespace Beeps.Features.Accounts;

/// <summary>Stable error codes for account flows. Published codes must never change.</summary>
public static class AccountErrorCodes
{
    public const string InvalidInput = "invalid_input";
    public const string CaptchaFailed = "captcha_failed";

    /// <summary>Signing in to an account past its wrong passwords, which takes Turnstile from then on.</summary>
    public const string CaptchaRequired = "captcha_required";
    public const string RegistrationClosed = "registration_closed";
    public const string InviteInvalid = "invite_invalid";
    public const string UsernameTaken = "username_taken";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountBanned = "account_banned";
    public const string CodeInvalid = "code_invalid";
    public const string AlreadyVerified = "already_verified";

    /// <summary>Signing in with a provider: it gave no verified address to make an account with.</summary>
    public const string ExternalNoEmail = "external_no_email";

    /// <summary>Signing up with a provider: its address is a throwaway one (<see cref="IDisposableEmailDomains"/>).</summary>
    public const string DisposableEmail = "disposable_email";

    /// <summary>The provider's account is linked to another account here, or this one has another of that provider.</summary>
    public const string ExternalLinked = "external_linked";

    /// <summary>The last way left to sign in, which cannot be unlinked before a password is set.</summary>
    public const string LastSignInMethod = "last_sign_in_method";

    /// <summary>
    /// Moving an account without a password to another address: it sets one first, so that the session alone cannot
    /// take the account to an address of someone else's.
    /// </summary>
    public const string PasswordNeeded = "password_needed";

    /// <summary>What the authenticator answered did not hold up, came too late, or is for a passkey nobody has here.</summary>
    public const string PasskeyFailed = "passkey_failed";

    /// <summary>The account's passkeys are a second step after the password, not a way to sign in by themselves.</summary>
    public const string PasskeyTwoFactor = "passkey_two_factor";

    /// <summary>The account has as many passkeys as it may (<see cref="AccountService.MaxPasskeys"/>).</summary>
    public const string PasskeysFull = "passkeys_full";

    /// <summary>Two-step sign-in with passkeys needs a passkey first.</summary>
    public const string NoPasskeys = "no_passkeys";
}

/// <summary>Names of unique indexes whose violations the account services translate into errors.</summary>
public static class AccountIndexes
{
    public const string UniqueHandle = "ux_actors_normalized_handle";
    public const string UniqueEmail = "ux_users_normalized_email";
    public const string UniqueLogin = "pk_user_logins";
    public const string UniqueLoginProvider = "ux_user_logins_user_id_provider";
    public const string UniquePasskey = "ux_user_passkeys_credential_id";
}

/// <summary>Form field names used in validation errors; the pages bind inputs with the same names.</summary>
public static class AccountFields
{
    public const string Email = "Email";
    public const string Username = "Username";
    public const string Password = "Password";
    public const string InviteCode = "InviteCode";
    public const string Code = "Code";
    public const string Login = "Login";
    public const string CurrentPassword = "CurrentPassword";
    public const string NewPassword = "NewPassword";
    public const string NewEmail = "NewEmail";
}
