namespace Beeps.Features.Accounts;

/// <summary>Stable error codes for account flows. Published codes must never change.</summary>
public static class AccountErrorCodes
{
    public const string InvalidInput = "invalid_input";
    public const string CaptchaFailed = "captcha_failed";
    public const string RegistrationClosed = "registration_closed";
    public const string InviteInvalid = "invite_invalid";
    public const string UsernameTaken = "username_taken";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountBanned = "account_banned";
    public const string CodeInvalid = "code_invalid";
    public const string AlreadyVerified = "already_verified";
}

/// <summary>Names of unique indexes whose violations the account services translate into errors.</summary>
public static class AccountIndexes
{
    public const string UniqueHandle = "ux_actors_normalized_handle";
    public const string UniqueEmail = "ux_users_normalized_email";
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
}
