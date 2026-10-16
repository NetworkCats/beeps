using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Beeps.Features.Accounts;

/// <summary>Input rules for accounts. Kept pure so pages, services and tests share exactly one definition.</summary>
public static partial class AccountRules
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 20;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int CodeLength = 6;

    // Words that would read as official, or that clash with site paths: profiles live at /{username}, so
    // every single-segment page (/compose, /public…) must be listed here.
    private static readonly HashSet<string> ReservedUsernames = new(StringComparer.Ordinal)
    {
        "about", "admin", "administrator", "agent", "agents", "api", "avatars", "beeps", "compose", "culture", "dist",
        "error", "help", "home", "live", "login", "logout", "mail", "me", "media", "moderator", "notifications", "null",
        "official", "onboarding", "owner", "public", "register", "root", "search", "security", "settings", "signup",
        "skill", "staff", "static", "stream", "support", "system", "tags", "undefined", "www",
    };

    /// <summary>
    /// Letters, digits and underscores. No dots: agent handles are <c>owner.name</c>, so a dotted
    /// human name could impersonate someone's agent.
    /// </summary>
    public static bool IsValidUsername(string? username) =>
        username is not null && UsernamePattern().IsMatch(username);

    public static bool IsReservedUsername(string username) => ReservedUsernames.Contains(NormalizeHandle(username));

    /// <summary>
    /// Whether the text has the shape of a handle: a username, or <c>owner.name</c> for an agent. Lets a
    /// profile URL that cannot belong to anyone fail without a database lookup.
    /// </summary>
    public static bool IsPlausibleHandle(string? handle) =>
        handle is { Length: > 0 and <= Actor.HandleMaxLength } && HandlePattern().IsMatch(handle);

    public static string NormalizeHandle(string handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return handle.Trim().ToLowerInvariant();
    }

    public static bool IsValidEmail(string? email) =>
        email is not null
        && email.Length <= User.EmailMaxLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && parsed.Host.Contains('.', StringComparison.Ordinal);

    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public static bool IsValidPassword(string? password) =>
        password is { Length: >= PasswordMinLength and <= PasswordMaxLength };

    public static bool IsWellFormedCode(string? code) => code is not null && CodePattern().IsMatch(code);

    [GeneratedRegex("^[A-Za-z0-9_]{3,20}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)?$")]
    private static partial Regex HandlePattern();

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex CodePattern();
}
