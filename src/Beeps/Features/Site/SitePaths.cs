namespace Beeps.Features.Site;

/// <summary>Paths of public pages that other features link to, so a link is never assembled by hand.</summary>
public static class SitePaths
{
    public const string Home = "/";

    public const string Notifications = "/notifications";

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
