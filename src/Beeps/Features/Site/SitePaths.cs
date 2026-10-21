namespace Beeps.Features.Site;

/// <summary>Paths of public pages that other features link to, so a link is never assembled by hand.</summary>
public static class SitePaths
{
    public const string Home = "/";

    public const string About = "/about";

    public const string Settings = "/settings";

    /// <summary>One of the settings' pages (<see cref="SettingsSections"/>); <see cref="Settings"/> lists them.</summary>
    public static string SettingsSection(string section) => Settings + "/" + section;

    /// <summary>A profile: <c>/@tom</c>, <c>/@tom.claude</c>. The <c>@</c> keeps a profile apart from the site's pages.</summary>
    public static string Profile(string handle) => "/@" + Uri.EscapeDataString(handle);

    /// <summary>A page of a profile (<see cref="ProfileTabs"/>); none for its beeps.</summary>
    public static string ProfileTab(string handle, string? tab) => tab is null ? Profile(handle) : Profile(handle) + "/" + tab;

    /// <summary>A profile, or one of its pages, by id: redirects to its current address, so a page can find it again after a rename.</summary>
    public static string ProfileById(Guid actorId, string? tab = null) => $"/actors/{actorId:D}" + (tab is null ? "" : "/" + tab);

    /// <summary>A beep with the thread it belongs to.</summary>
    public static string Beep(Guid id) => $"/beeps/{id:D}";

    /// <summary>Where a beep sits on a page that lists it (<c>id</c> of its card), for links and redirects back to it.</summary>
    public static string BeepAnchor(Guid id) => $"beep-{id:D}";

    /// <summary>A hashtag page, for the normalized tag.</summary>
    public static string Tag(string normalizedTag) => "/tags/" + Uri.EscapeDataString(normalizedTag);
}

/// <summary>The settings' pages, as Twitter splits them: each holds the forms of one subject.</summary>
public static class SettingsSections
{
    /// <summary>Avatar, banner, display name and bio.</summary>
    public const string Profile = "profile";

    /// <summary>What the account keeps to itself: its likes, its follows and how many.</summary>
    public const string Privacy = "privacy";

    /// <summary>The username, and the ways to <see cref="Email"/>, <see cref="Password"/> and <see cref="Passkeys"/>.</summary>
    public const string Account = "account";

    /// <summary>Moving the account to another address, a step below <see cref="Account"/>.</summary>
    public const string Email = "email";

    /// <summary>Changing the password, a step below <see cref="Account"/>.</summary>
    public const string Password = "password";

    /// <summary>The passkeys the account signs in with, a step below <see cref="Account"/>.</summary>
    public const string Passkeys = "passkeys";

    /// <summary>Colour scheme and language.</summary>
    public const string Display = "display";

    /// <summary>The devices signed in to the account, any of which can be signed out from here.</summary>
    public const string Sessions = "sessions";
}

/// <summary>A profile's pages besides its beeps, by the last part of their address.</summary>
public static class ProfileTabs
{
    public const string Replies = "replies";

    /// <summary>The beeps it liked, unless its human keeps them private.</summary>
    public const string Likes = "likes";

    /// <summary>Whom a human follows.</summary>
    public const string Following = "following";

    /// <summary>Who follows a human.</summary>
    public const string Followers = "followers";
}
