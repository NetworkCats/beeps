using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beeps.Infrastructure.Previews;

/// <summary>
/// The typefaces previews are drawn with, and for each character the first of them that has it. The native Skia on
/// Linux finds no system fonts (it is built without fontconfig), so the fonts are files of their own
/// (<see cref="PreviewOptions.FontDirectory"/>), each loaded once and kept, with its shaper, for as long as the app runs.
/// </summary>
public sealed partial class PreviewFonts : IDisposable
{
    /// <summary>
    /// In the order they are tried: Latin, Greek and Cyrillic; then Chinese, Japanese and Korean (Han characters in their
    /// Simplified Chinese forms); then emoji, which are tried first for a character drawn as an emoji.
    /// </summary>
    internal static readonly string[] Families = ["Noto Sans", "Noto Sans CJK SC", "Noto Color Emoji"];

    /// <summary>All but the last, emoji.</summary>
    private const int TextFamilies = 2;

    /// <summary>More faces than any collection the image holds (a CJK one has ten); past the last, loading gives null.</summary>
    private const int MaxFacesPerFile = 32;

    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc"];

    private readonly SKTypeface[] regular;
    private readonly SKTypeface[] bold;
    private readonly SKTypeface? emoji;
    private readonly List<SKTypeface> owned = [];
    private readonly ConcurrentDictionary<(int Rune, bool Bold), SKTypeface?> system = new();
    private readonly ConcurrentDictionary<SKTypeface, SKShaper?> shapers = new();
    private readonly ConcurrentDictionary<SKTypeface, SKFont> probes = new();

    public PreviewFonts(IOptions<PreviewOptions> options, ILogger<PreviewFonts> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = options.Value.FontDirectory;
        var files = Load(directory);
        foreach (var family in Families.Where(f => !files.ContainsKey((f, false))))
        {
            LogFontMissing(logger, family, directory);
        }

        regular = Faces(files, bold: false);
        bold = Faces(files, bold: true);
        emoji = Find(files, Families[^1], bold: false) ?? SKFontManager.Default.MatchCharacter(0x1F600);
    }

    /// <summary>What the text is in, unless one of its characters is not there; an ellipsis is always drawn with it.</summary>
    public SKTypeface Primary(bool isBold) => (isBold ? bold : regular)[0];

    /// <summary>The typeface for one character as the reader sees it (a text element: a letter with its marks, an emoji sequence).</summary>
    public SKTypeface For(string textElement, bool isBold)
    {
        ArgumentException.ThrowIfNullOrEmpty(textElement);
        var rune = Rune.GetRuneAt(textElement, 0).Value;
        if (emoji is not null && IsEmoji(textElement) && Has(emoji, rune))
        {
            return emoji;
        }

        var faces = isBold ? bold : regular;
        foreach (var face in faces)
        {
            if (Has(face, rune))
            {
                return face;
            }
        }

        if (emoji is not null && Has(emoji, rune))
        {
            return emoji;
        }

        var style = isBold ? SKFontStyle.Bold : SKFontStyle.Normal;
        return system.GetOrAdd((rune, isBold), key => SKFontManager.Default.MatchCharacter(null, style, null, key.Rune)) ?? faces[0];
    }

    /// <summary>
    /// The shaper for a typeface <see cref="For"/> gave; shaping with it is safe from any thread (HarfBuzz). Null for one
    /// with no font data, the empty default where there are no fonts at all: its text is left out.
    /// </summary>
    public SKShaper? Shaper(SKTypeface typeface) => shapers.GetOrAdd(typeface, face =>
    {
        using var data = face.OpenStream();
        return data is null ? null : new SKShaper(face);
    });

    public void Dispose()
    {
        foreach (var shaper in shapers.Values)
        {
            shaper?.Dispose();
        }

        foreach (var probe in probes.Values)
        {
            probe.Dispose();
        }

        foreach (var face in owned)
        {
            face.Dispose();
        }
    }

    /// <summary>
    /// Drawn as an emoji rather than as text: a pictograph, or a sequence that asks for one (the emoji variation
    /// selector, a joiner, a skin tone, a keycap).
    /// </summary>
    internal static bool IsEmoji(string textElement)
    {
        foreach (var rune in textElement.EnumerateRunes())
        {
            if (rune.Value is 0xFE0F or 0x200D or 0x20E3 or (>= 0x1F000 and <= 0x1FAFF))
            {
                return true;
            }
        }

        return false;
    }

    private bool Has(SKTypeface face, int rune) => probes.GetOrAdd(face, f => new SKFont(f)).ContainsGlyph(rune);

    /// <summary>Every face in the directory's font files, by family and whether it is bold.</summary>
    private Dictionary<(string Family, bool Bold), SKTypeface> Load(string directory)
    {
        var faces = new Dictionary<(string, bool), SKTypeface>();
        if (!Directory.Exists(directory))
        {
            return faces;
        }

        foreach (var file in Directory.EnumerateFiles(directory).Where(f => FontExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            for (var index = 0; index < MaxFacesPerFile && SKTypeface.FromFile(file, index) is { } face; index++)
            {
                if (faces.TryAdd((face.FamilyName, face.FontWeight >= (int)SKFontStyleWeight.SemiBold), face))
                {
                    owned.Add(face);
                }
                else
                {
                    face.Dispose();
                }
            }
        }

        return faces;
    }

    private static SKTypeface? Find(Dictionary<(string Family, bool Bold), SKTypeface> files, string family, bool bold) =>
        files.GetValueOrDefault((family, bold)) ?? SKFontManager.Default.MatchFamily(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal);

    /// <summary>The text families that are there, in order; at least the system's default typeface.</summary>
    private static SKTypeface[] Faces(Dictionary<(string Family, bool Bold), SKTypeface> files, bool bold)
    {
        var faces = Families.Take(TextFamilies).Select(family => Find(files, family, bold)).OfType<SKTypeface>().ToArray();
        return faces.Length > 0 ? faces : [SKFontManager.Default.MatchFamily(null, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No {Family} font in {Directory}: link previews are drawn with the system's fonts, or leave out what none has")]
    private static partial void LogFontMissing(ILogger logger, string family, string directory);
}
