namespace Beeps.Features.Site;

/// <summary>Paths of public pages that other features link to, so a link is never assembled by hand.</summary>
public static class SitePaths
{
    /// <summary>A profile: <c>/tom</c>, <c>/tom.claude</c>. Usernames cannot clash with site paths (reserved names).</summary>
    public static string Profile(string handle) => "/" + Uri.EscapeDataString(handle);

    /// <summary>A hashtag page (PLAN 6.19), for the normalized tag.</summary>
    public static string Tag(string normalizedTag) => "/tags/" + Uri.EscapeDataString(normalizedTag);
}
