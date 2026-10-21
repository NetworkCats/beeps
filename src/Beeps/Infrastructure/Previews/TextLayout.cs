using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beeps.Infrastructure.Previews;

/// <summary>A run of text in one colour, as laid out by <see cref="TextLayout"/>.</summary>
public readonly record struct TextSpan(string Text, SKColor Color);

/// <summary>
/// Text set in lines for a preview: each character in the first font that has it (<see cref="PreviewFonts"/>), shaped
/// with HarfBuzz, and wrapped at spaces, between Chinese and Japanese characters (but not before their closing
/// punctuation or after their opening), and after a hyphen or slash; a word too long for a line is broken anywhere.
/// What does not fit in the lines allowed ends in an ellipsis.
/// </summary>
public sealed class TextLayout
{
    private const string Ellipsis = "…";

    // No line starts with these or ends with those (kinsoku): closing and opening punctuation, small kana, the
    // prolonged sound mark, and their Latin counterparts.
    private const string NoLineStart = "、。，．：；？！）」』】》〉〕〗〙〛｝］〞’”…‥ー々ゝゞぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ)]}.,;:!?%";
    private const string NoLineEnd = "（「『【《〈〔〖〘〚｛［〝‘“([{";

    private readonly PreviewFonts fonts;
    private readonly bool bold;
    private readonly float size;
    private readonly List<Line> lines;

    private TextLayout(PreviewFonts fonts, bool bold, float size, List<Line> lines, bool truncated)
    {
        (this.fonts, this.bold, this.size, this.lines) = (fonts, bold, size, lines);
        Truncated = truncated;
    }

    public int LineCount => lines.Count;

    /// <summary>The text did not fit: the last line ends in an ellipsis.</summary>
    public bool Truncated { get; }

    /// <summary>The widest line's width.</summary>
    public float Width => lines.Count == 0 ? 0 : lines.Max(l => l.Width);

    /// <summary>Sets <paramref name="spans"/> in at most <paramref name="maxLines"/> lines of <paramref name="maxWidth"/>.</summary>
    public static TextLayout Create(PreviewFonts fonts, IEnumerable<TextSpan> spans, bool bold, float size, float maxWidth, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        var clusters = Clusters(fonts, spans, bold);
        Measure(fonts, clusters, size);
        var lines = Wrap(clusters, maxWidth);
        var truncated = lines.Count > maxLines;
        if (truncated)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            lines[^1] = WithEllipsis(fonts, lines[^1], bold, size, maxWidth);
        }

        return new TextLayout(fonts, bold, size, lines, truncated);
    }

    /// <summary>Draws the lines one below the other, from <paramref name="x"/>, each with its capitals centred in its line box.</summary>
    public void Draw(SKCanvas canvas, float x, float top, float lineHeight)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        float capHeight;
        using (var primary = Font(fonts.Primary(bold), size))
        {
            capHeight = primary.Metrics.CapHeight;
        }

        using var paint = new SKPaint { IsAntialias = true };
        var baseline = top + ((lineHeight + capHeight) / 2);
        foreach (var line in lines)
        {
            var start = x;
            foreach (var run in Runs(line.Clusters))
            {
                using var font = Font(run.Face, size);
                var shaped = Shape(fonts, run, font);
                if (shaped.Codepoints.Length > 0)
                {
                    using var builder = new SKTextBlobBuilder();
                    builder.AddPositionedRun([.. shaped.Codepoints.Select(g => (ushort)g)], font, shaped.Points);
                    using var blob = builder.Build();
                    paint.Color = run.Color;
                    canvas.DrawText(blob, start, baseline, paint);
                }

                start += shaped.Width;
            }

            baseline += lineHeight;
        }
    }

    /// <summary>
    /// Shaped from UTF-16, so the glyphs' clusters are indexes into the run's string (shaping a string as such gives
    /// UTF-8 offsets).
    /// </summary>
    private static SKShaper.Result Shape(PreviewFonts fonts, Run run, SKFont font)
    {
        if (fonts.Shaper(run.Face) is not { } shaper)
        {
            return new SKShaper.Result([], [], [], 0);
        }

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(run.Text);
        buffer.GuessSegmentProperties();
        return shaper.Shape(buffer, font);
    }

    /// <summary>
    /// How text elements of the given advances wrap, for testing the breaking rules apart from any font: each line's
    /// text.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(IEnumerable<string> elements, Func<string, float> advance, float maxWidth)
    {
        var clusters = elements.Select(text => new Cluster(text, SKColors.Black, SKTypeface.Default, KindOf(text)) { Advance = advance(text) }).ToList();
        return [.. Wrap(clusters, maxWidth).Select(line => string.Concat(line.Clusters.Select(c => c.Text)))];
    }

    private static SKFont Font(SKTypeface face, float size) =>
        new(face, size) { Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };

    private static List<Cluster> Clusters(PreviewFonts fonts, IEnumerable<TextSpan> spans, bool bold)
    {
        var clusters = new List<Cluster>();
        foreach (var span in spans)
        {
            var elements = StringInfo.GetTextElementEnumerator(span.Text);
            while (elements.MoveNext())
            {
                var text = elements.GetTextElement();
                var kind = KindOf(text);
                // A space between runs in other fonts stays in the text's own, so it does not split them further.
                var face = kind == Kind.Text ? fonts.For(text, bold) : fonts.Primary(bold);
                clusters.Add(new Cluster(kind == Kind.Space ? " " : text, span.Color, face, kind));
            }
        }

        return clusters;
    }

    private static Kind KindOf(string textElement) =>
        textElement is "\n" or "\r\n" or "\r" ? Kind.Newline : string.IsNullOrWhiteSpace(textElement) ? Kind.Space : Kind.Text;

    /// <summary>Each cluster's advance, from shaping each run of one font and colour as a whole (so kerning counts).</summary>
    private static void Measure(PreviewFonts fonts, List<Cluster> clusters, float size)
    {
        var start = 0;
        foreach (var run in Runs(clusters))
        {
            var count = run.Count;
            var offsets = new int[count];
            for (int i = 0, offset = 0; i < count; offset += clusters[start + i].Text.Length, i++)
            {
                offsets[i] = offset;
            }

            using var font = Font(run.Face, size);
            var shaped = Shape(fonts, run, font);
            for (var g = 0; g < shaped.Codepoints.Length; g++)
            {
                var advance = (g + 1 < shaped.Points.Length ? shaped.Points[g + 1].X : shaped.Width) - shaped.Points[g].X;
                // A glyph belongs to the cluster its text starts in; one made of several (a ligature) counts for the first.
                var index = Array.BinarySearch(offsets, (int)shaped.Clusters[g]);
                index = index >= 0 ? index : ~index - 1;
                clusters[start + index].Advance += Math.Abs(advance);
            }

            start += count;
        }
    }

    private static List<Line> Wrap(List<Cluster> clusters, float maxWidth)
    {
        var lines = new List<Line>();
        var current = new List<Cluster>();
        foreach (var cluster in clusters)
        {
            if (cluster.Kind == Kind.Newline)
            {
                lines.Add(new Line(Trimmed(current)));
                current = [];
                continue;
            }

            // Spaces hang past the end of a line, and none starts a line the text wrapped onto.
            if (cluster.Kind == Kind.Space && current.Count == 0 && lines.Count > 0 && !lines[^1].EndsParagraph)
            {
                continue;
            }

            current.Add(cluster);
            while (current.Count > 1 && WidthOf(Trimmed(current)) > maxWidth)
            {
                var at = BreakAt(current, maxWidth);
                lines.Add(new Line(Trimmed(current[..at]), EndsParagraph: false));
                current = [.. current[at..].SkipWhile(c => c.Kind == Kind.Space)];
            }
        }

        lines.Add(new Line(Trimmed(current)));
        return lines;
    }

    /// <summary>
    /// The last place <paramref name="line"/> may break with what comes before it fitting; failing that, after as much
    /// as fits, and at least one cluster.
    /// </summary>
    private static int BreakAt(List<Cluster> line, float maxWidth)
    {
        var (at, fit) = (0, 1);
        var (width, advance) = (0f, 0f);
        for (var i = 1; i < line.Count; i++)
        {
            advance += line[i - 1].Advance;
            if (line[i - 1].Kind != Kind.Space)
            {
                width = advance;
            }

            if (width > maxWidth)
            {
                break;
            }

            fit = i;
            if (BreaksBetween(line[i - 1], line[i]))
            {
                at = i;
            }
        }

        return at > 0 ? at : fit;
    }

    private static bool BreaksBetween(Cluster before, Cluster after)
    {
        if (before.Kind == Kind.Space || after.Kind == Kind.Space)
        {
            return before.Kind == Kind.Space && after.Kind != Kind.Space;
        }

        if (NoLineStart.Contains(after.Text, StringComparison.Ordinal) || NoLineEnd.Contains(before.Text, StringComparison.Ordinal))
        {
            return false;
        }

        return IsWide(before.Text) || IsWide(after.Text) || PreviewFonts.IsEmoji(before.Text) || PreviewFonts.IsEmoji(after.Text) || before.Text is "-" or "/";
    }

    /// <summary>Chinese, Japanese and Korean, and full-width forms: lines may break between any two of them.</summary>
    private static bool IsWide(string textElement) =>
        Rune.GetRuneAt(textElement, 0).Value is (>= 0x2E80 and <= 0x9FFF) or (>= 0xAC00 and <= 0xD7AF) or (>= 0xF900 and <= 0xFAFF)
            or (>= 0xFF00 and <= 0xFFEF) or (>= 0x20000 and <= 0x3FFFF);

    private static Line WithEllipsis(PreviewFonts fonts, Line line, bool bold, float size, float maxWidth)
    {
        var color = line.Clusters.Count > 0 ? line.Clusters[^1].Color : SKColors.Black;
        var ellipsis = new Cluster(Ellipsis, color, fonts.Primary(bold), Kind.Text);
        Measure(fonts, [ellipsis], size);
        var kept = line.Clusters.ToList();
        while (kept.Count > 0 && WidthOf(kept) + ellipsis.Advance > maxWidth)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return new Line([.. Trimmed(kept), ellipsis]);
    }

    private static List<Cluster> Trimmed(List<Cluster> clusters)
    {
        var end = clusters.Count;
        while (end > 0 && clusters[end - 1].Kind == Kind.Space)
        {
            end--;
        }

        return clusters[..end];
    }

    private static float WidthOf(List<Cluster> clusters) => clusters.Sum(c => c.Advance);

    /// <summary>The clusters in runs of one font and colour, which are shaped and drawn as one.</summary>
    private static IEnumerable<Run> Runs(List<Cluster> clusters)
    {
        var start = 0;
        while (start < clusters.Count)
        {
            var first = clusters[start];
            var end = start + 1;
            while (end < clusters.Count && clusters[end].Face == first.Face && clusters[end].Color == first.Color)
            {
                end++;
            }

            var text = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                text.Append(clusters[i].Text);
            }

            yield return new Run(text.ToString(), first.Face, first.Color, end - start);
            start = end;
        }
    }

    private enum Kind
    {
        Text,
        Space,
        Newline,
    }

    private sealed class Cluster(string text, SKColor color, SKTypeface face, Kind kind)
    {
        public string Text { get; } = text;

        public SKColor Color { get; } = color;

        public SKTypeface Face { get; } = face;

        public Kind Kind { get; } = kind;

        public float Advance { get; set; }
    }

    private readonly record struct Run(string Text, SKTypeface Face, SKColor Color, int Count);

    /// <param name="EndsParagraph">Ended by a line break in the text, rather than wrapped.</param>
    private sealed record Line(List<Cluster> Clusters, bool EndsParagraph = true)
    {
        public float Width => Clusters.Sum(c => c.Advance);
    }
}
