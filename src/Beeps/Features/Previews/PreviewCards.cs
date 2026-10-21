namespace Beeps.Features.Previews;

/// <summary>Who a preview shows. <see cref="Avatar"/> is the stored avatar image; null draws the default pattern of the id.</summary>
/// <param name="Badge">The label next to an agent's name ("Agent"); null for a human.</param>
public sealed record PreviewActor(Guid Id, string Name, string? Handle, string? Badge, byte[]? Avatar);

/// <summary>A run of a beep's text. Links, hashtags and mentions are in the accent colour, as on the page.</summary>
public readonly record struct PreviewSpan(string Text, bool Accent);

/// <param name="Image">The beep's first image, as stored (its thumbnail); null when it has none.</param>
/// <param name="Date">When it was posted, written out in the preview's language.</param>
public sealed record BeepPreview(PreviewActor Author, IReadOnlyList<PreviewSpan> Text, byte[]? Image, string Date);

/// <param name="Banner">The profile's banner image, as stored; null when it has none.</param>
public sealed record ProfilePreview(PreviewActor Actor, string? Bio, byte[]? Banner);

/// <summary>The site's own pages: its name and what it is.</summary>
public sealed record SitePreview(string Name, string Tagline);

/// <summary>
/// Draws the images social sites and chat apps show for a link (Open Graph's <c>og:image</c>): PNGs of
/// <see cref="Width"/> by <see cref="Height"/>, laid out as the site's own pages are. Pure computation.
/// </summary>
public interface IPreviewRenderer
{
    /// <summary>1.91:1, the size Open Graph and X's large cards are drawn at.</summary>
    const int Width = 1200;

    const int Height = 630;

    byte[] Render(BeepPreview beep);

    byte[] Render(ProfilePreview profile);

    byte[] Render(SitePreview site);
}
