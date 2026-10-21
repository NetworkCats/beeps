namespace Beeps.Infrastructure.Previews;

public sealed class PreviewOptions
{
    public const string SectionName = "Previews";

    /// <summary>
    /// Where the fonts of <see cref="PreviewFonts.Families"/> are: the image copies them here (Dockerfile). One that is
    /// missing is taken from the system's fonts if it has it, and any character no font has from whatever system font
    /// does, as on a developer's Mac; failing both, it is drawn as a missing glyph.
    /// </summary>
    public string FontDirectory { get; set; } = "/usr/share/fonts/beeps";

    /// <summary>Where the images drawn are kept (<see cref="DiskPreviewStore"/>); empty for a folder in the system's temp directory.</summary>
    public string? Directory { get; set; }

    /// <summary>An image no one has asked for in this long is deleted (<see cref="PreviewCleanup"/>), and drawn again if asked for after.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the folder is swept for those.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromDays(1);

    public string ResolvedDirectory => string.IsNullOrWhiteSpace(Directory) ? Path.Combine(Path.GetTempPath(), "beeps-previews") : Directory;
}
