using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages;

/// <summary>
/// What a page says of itself to search engines and to the sites a link to it is shared on (Open Graph, X's cards), as
/// <c>ViewData[PageMeta.Key]</c>; the layout writes it out. A page without one is described as the site, with the
/// site's preview, and kept out of search results: only pages anyone can read, at their one address, are
/// <see cref="Indexed"/>.
/// </summary>
/// <param name="Description">Plain text: a sentence or two, or an <see cref="Excerpt"/>.</param>
/// <param name="Image">The page's preview image (Features/Previews/PreviewImages).</param>
public sealed partial record PageMeta(string Description, Uri Image)
{
    public const string Key = "Meta";

    /// <summary>About as much as search results and shared links show of a description.</summary>
    public const int DescriptionLength = 300;

    /// <summary>og:type: <c>website</c>, or what the page is (<c>article</c>, <c>profile</c>).</summary>
    public string Type { get; init; } = "website";

    /// <summary>og:title, where it is not the page's own title: the site's name is left out of it either way.</summary>
    public string? Title { get; init; }

    /// <summary>Listed in search results, at <see cref="Path"/>; never a later page of a list (a cursor in the query).</summary>
    public bool Indexed { get; init; }

    /// <summary>The page's own address, where the one asked for may differ from it (a handle written in other case).</summary>
    public string? Path { get; init; }

    /// <summary>The site's home page, which names the site to search engines (schema.org's WebSite).</summary>
    public bool IsHome { get; init; }

    /// <summary>When the beep was posted (article:published_time).</summary>
    public DateTimeOffset? Published { get; init; }

    /// <summary>A profile's handle (profile:username).</summary>
    public string? Username { get; init; }

    /// <summary>A page's title as the browser shows it: the page's own and the site's name, or the site's own title.</summary>
    public static string DocumentTitle(object? title, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return title?.ToString() is { Length: > 0 } own ? $"{own} · {localizer["Site.Name"]}" : localizer["Home.SiteTitle"];
    }

    /// <summary>
    /// The text on one line, at most <paramref name="length"/> characters as a reader counts them: if it is longer, cut
    /// at a space near the end if there is one (else anywhere, as in Chinese), with an ellipsis.
    /// </summary>
    public static string Excerpt(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = Whitespace().Replace(text, " ").Trim();
        var info = new StringInfo(line);
        if (info.LengthInTextElements <= length)
        {
            return line;
        }

        var cut = info.SubstringByTextElements(0, length - 1);
        var space = cut.LastIndexOf(' ');
        var midWord = info.SubstringByTextElements(length - 1, 1) != " ";
        return (midWord && space > cut.Length * 2 / 3 ? cut[..space] : cut).TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
