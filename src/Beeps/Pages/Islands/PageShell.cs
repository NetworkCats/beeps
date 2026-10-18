using System.Text.Json.Serialization;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Beeps.Pages.Islands;

/// <summary>What of the layout around a page's content is the page's own (<see cref="PageShell"/>).</summary>
/// <param name="Nav">The navigation's item for the page (<c>data-nav</c>), marked as the current one.</param>
/// <param name="Wide">A wider content column and no side column: the admin pages, which hold tables.</param>
/// <param name="Hidden">The parts of the layout the page leaves out (<c>data-part</c>), as it says that itself.</param>
/// <param name="Styles">
/// The stylesheets the page links, by name (<c>dist/{name}.css</c>, <c>data-style</c>): the layout links or fetches
/// ahead each of them, under the fingerprinted address the app then links it by.
/// </param>
public sealed record PageShellProps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Nav,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Wide,
    IReadOnlyList<string> Hidden,
    IReadOnlyList<string> Styles)
{
    public string? Current(string nav) => Nav == nav ? "page" : null;

    /// <summary>Razor leaves out an attribute whose value is null: <c>hidden="@shell.Hide(…)"</c>.</summary>
    public string? Hide(string part) => Hidden.Contains(part) ? "" : null;
}

/// <summary>
/// What changes in the layout from one page to the next (<c>_Layout.cshtml</c>). The layout draws it, and a page's data
/// (<see cref="IslandRequests"/>) says it, so the app can show another page in place of this one from JSON
/// (<c>Client/app/router.ts</c>): the layout around the content stays.
/// </summary>
public static class PageShell
{
    public const string SearchPart = "search";
    public const string AboutPart = "about";
    public const string ComposePart = "compose";

    /// <summary>ViewData: the page edits an avatar or a banner, and links the cropper's stylesheet.</summary>
    public const string ImageCrop = "ImageCrop";

    /// <summary>ViewData: the layout around no page yet (<c>_AppShell.cshtml</c>); its content and the app's config come as data.</summary>
    public const string AppShell = "AppShell";

    public static PageShellProps For(ViewContext view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var context = view.HttpContext;
        var user = context.User;
        var signedIn = user.IsHuman();
        var page = view.RouteData.Values["page"] as string;
        var adminPage = page?.StartsWith("/Admin/", StringComparison.Ordinal) == true;
        var nav = page switch
        {
            // Signed out, the home page is the public timeline.
            "/Index" => signedIn ? "home" : "public",
            "/Public" => "public",
            "/Search" => "search",
            "/Notifications" => "notifications",
            "/Agents/Index" or "/Agents/Edit" => "agents",
            "/Settings" => "settings",
            "/Profiles/Index" when signedIn && OnOwnProfile(context) => "profile",
            _ => null,
        };
        // The search page has a search box of its own, the about page says all the side column's card does, and the
        // compose page needs no button to itself.
        var hidden = page switch
        {
            "/Search" => new[] { SearchPart },
            "/About" => [AboutPart],
            "/Compose" => [ComposePart],
            _ => [],
        };
        var styles = new List<string> { "main" };
        if (signedIn)
        {
            styles.Add("member");
        }

        if (adminPage)
        {
            styles.Add("admin");
        }

        if (view.ViewData[ImageCrop] is true)
        {
            styles.Add("image-crop");
        }

        return new PageShellProps(nav, adminPage, hidden, styles);
    }

    /// <summary>The shape of the skeleton (<c>_Skeleton.cshtml</c>) an app shell shows until the page's data has come.</summary>
    public static string Skeleton(ViewContext view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var page = view.RouteData.Values["page"] as string ?? "";
        return page switch
        {
            _ when page.StartsWith("/Admin/", StringComparison.Ordinal) => "table",
            "/Index" or "/Public" or "/Tags/Index" or "/Beep/Details" => "feed",
            "/Profiles/Index" => "profile",
            "/Settings" when view.RouteData.Values["section"] is null => "list",
            "/Notifications" or "/Search" or "/Agents/Index" => "list",
            _ => "form",
        };
    }

    private static bool OnOwnProfile(HttpContext context) =>
        string.Equals(context.Request.Path.Value?.TrimStart('/').Split('/')[0], context.User.Identity?.Name, StringComparison.OrdinalIgnoreCase);
}
