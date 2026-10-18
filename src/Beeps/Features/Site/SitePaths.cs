namespace Beeps.Features.Site;

/// <summary>Paths of public pages that other features link to, so a link is never assembled by hand.</summary>
public static class SitePaths
{
    public const string Home = "/";

    public const string Notifications = "/notifications";

    public const string About = "/about";

    public const string Settings = "/settings";

    /// <summary>One of the settings' pages (<see cref="SettingsSections"/>); <see cref="Settings"/> lists them.</summary>
    public static string SettingsSection(string section) => Settings + "/" + section;

    /// <summary>A profile: <c>/tom</c>, <c>/tom.claude</c>. Usernames cannot clash with site paths (reserved names).</summary>
    public static string Profile(string handle) => "/" + Uri.EscapeDataString(handle);

    /// <summary>The replies tab of a profile.</summary>
    public static string ProfileReplies(string handle) => Profile(handle) + "/replies";

    /// <summary>A profile by id: redirects to its current address, so a page can find it again after a rename.</summary>
    public static string ProfileById(Guid actorId, bool replies = false) => $"/actors/{actorId:D}" + (replies ? "/replies" : "");

    /// <summary>A beep with the thread it belongs to.</summary>
    public static string Beep(Guid id) => $"/beeps/{id:D}";

    /// <summary>Where a beep sits on a page that lists it (<c>id</c> of its card), for links and redirects back to it.</summary>
    public static string BeepAnchor(Guid id) => $"beep-{id:D}";

    /// <summary>A hashtag page (PLAN 6.19), for the normalized tag.</summary>
    public static string Tag(string normalizedTag) => "/tags/" + Uri.EscapeDataString(normalizedTag);
}

/// <summary>The settings' pages, as Twitter splits them: each holds the forms of one subject.</summary>
public static class SettingsSections
{
    /// <summary>Avatar, banner, display name and bio.</summary>
    public const string Profile = "profile";

    /// <summary>The username, and the ways to <see cref="Email"/> and <see cref="Password"/>.</summary>
    public const string Account = "account";

    /// <summary>Moving the account to another address, a step below <see cref="Account"/>.</summary>
    public const string Email = "email";

    /// <summary>Changing the password, a step below <see cref="Account"/>.</summary>
    public const string Password = "password";

    /// <summary>Colour scheme and language.</summary>
    public const string Display = "display";
}
