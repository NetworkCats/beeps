using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Beeps.Features.Accounts;
using Beeps.Features.Shared;
using Beeps.Features.Site;

namespace Beeps.Features.Beeps;

public enum BeepTextTokenKind
{
    Hashtag,
    Mention,
}

/// <param name="Start">UTF-16 index of the <c>#</c> or <c>@</c>.</param>
/// <param name="Length">Including the <c>#</c> or <c>@</c>.</param>
/// <param name="Value">What follows the <c>#</c> or <c>@</c>, as written.</param>
public readonly record struct BeepTextToken(BeepTextTokenKind Kind, int Start, int Length, string Value);

public enum BeepTextProblem
{
    None,
    Empty,
    TooLong,
    ForbiddenCharacters,
    HashtagTooLong,
    TooManyHashtags,
    TooManyMentions,
}

/// <param name="Hashtags">Normalized, distinct, in order of appearance.</param>
/// <param name="MentionHandles">Normalized handles, distinct, in order of appearance; not yet resolved to actors.</param>
public sealed record ParsedBeepText(string Text, BeepTextProblem Problem, IReadOnlyList<string> Hashtags, IReadOnlyList<string> MentionHandles)
{
    public bool IsValid => Problem == BeepTextProblem.None;
}

/// <summary>
/// The only implementation of beep text rules (PLAN 6.19, 12): length, #hashtags, @mentions and HTML
/// rendering. Pages, SSE fragments and the API all go through here.
/// </summary>
public static class BeepText
{
    /// <summary>In user-perceived characters (grapheme clusters): one Chinese character or one emoji counts as 1.</summary>
    public const int MaxLength = 500;

    /// <summary>A hard cap in UTF-16 code units, against graphemes stacked with endless combining marks.</summary>
    public const int MaxStorageLength = 8000;

    public const int MaxHashtags = 10;

    /// <summary>In user-perceived characters, after normalization.</summary>
    public const int MaxHashtagLength = 50;

    public const int MaxHashtagStorageLength = 400;

    public const int MaxMentions = 10;

    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>One kind of line break, no surrounding whitespace.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ReplaceLineEndings("\n").Trim();
    }

    public static int Length(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>Case- and width-insensitive form used for storage and matching (NFKC, lower case); display keeps the author's spelling.</summary>
    public static string NormalizeHashtag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return tag.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }

    /// <summary>Normalizes and checks text about to be posted.</summary>
    public static ParsedBeepText Parse(string? raw)
    {
        var text = Normalize(raw ?? "");
        if (text.Length == 0)
        {
            return Rejected(text, BeepTextProblem.Empty);
        }

        if (TextRules.HasForbiddenCharacters(text, allowLineBreaks: true))
        {
            return Rejected(text, BeepTextProblem.ForbiddenCharacters);
        }

        if (text.Length > MaxStorageLength || Length(text) > MaxLength)
        {
            return Rejected(text, BeepTextProblem.TooLong);
        }

        var tokens = Tokenize(text);
        var hashtags = Hashtags(tokens);
        if (hashtags.Any(t => Length(t) > MaxHashtagLength || t.Length > MaxHashtagStorageLength))
        {
            return Rejected(text, BeepTextProblem.HashtagTooLong);
        }

        if (hashtags.Count > MaxHashtags)
        {
            return Rejected(text, BeepTextProblem.TooManyHashtags);
        }

        var mentions = tokens
            .Where(t => t.Kind == BeepTextTokenKind.Mention)
            .Select(t => AccountRules.NormalizeHandle(t.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return mentions.Count > MaxMentions
            ? Rejected(text, BeepTextProblem.TooManyMentions)
            : new ParsedBeepText(text, BeepTextProblem.None, hashtags, mentions);
    }

    /// <summary>Reads a hashtag given on its own (a URL segment), with or without its <c>#</c>.</summary>
    public static bool TryNormalizeHashtag(string? input, out string normalized)
    {
        normalized = "";
        var trimmed = input?.Trim() ?? "";
        var tag = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
        if (tag.Length == 0 || tag.Length > MaxHashtagStorageLength)
        {
            return false;
        }

        var tokens = Tokenize("#" + tag);
        if (tokens.Count != 1 || tokens[0].Length != tag.Length + 1)
        {
            return false;
        }

        normalized = NormalizeHashtag(tag);
        return Length(normalized) <= MaxHashtagLength;
    }

    /// <summary>The normalized hashtags of stored text, for API responses.</summary>
    public static IReadOnlyList<string> Hashtags(string text) => Hashtags(Tokenize(text));

    /// <summary>
    /// Finds #hashtags and @mentions. Either must start the text or follow a character that is not part of
    /// a word (so <c>a@b.com</c> and <c>page#2</c> are neither). A hashtag is letters, digits, marks and
    /// underscores with at least one letter, and ends at a space or punctuation; a mention is a username,
    /// optionally followed by <c>.name</c> for an agent.
    /// </summary>
    public static IReadOnlyList<BeepTextToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<BeepTextToken>();
        var i = 0;
        while (i < text.Length)
        {
            var sigil = text[i];
            if ((sigil == '#' || sigil == '@') && StartsToken(text, i))
            {
                var length = sigil == '#' ? HashtagLength(text, i + 1) : MentionLength(text, i + 1);
                if (length > 0)
                {
                    var kind = sigil == '#' ? BeepTextTokenKind.Hashtag : BeepTextTokenKind.Mention;
                    tokens.Add(new BeepTextToken(kind, i, length + 1, text.Substring(i + 1, length)));
                    i += length + 1;
                    continue;
                }
            }

            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Safe HTML for a beep: the text is escaped first, then hashtags and the mentions in
    /// <paramref name="linkedHandles"/> (normalized handles of actors that exist) become links.
    /// </summary>
    public static string RenderHtml(string text, IReadOnlySet<string> linkedHandles)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(linkedHandles);
        var html = new StringBuilder(text.Length + 64);
        var position = 0;
        foreach (var token in Tokenize(text))
        {
            AppendText(html, text.AsSpan(position, token.Start - position));
            var written = text.AsSpan(token.Start, token.Length);
            var path = token.Kind == BeepTextTokenKind.Hashtag
                ? SitePaths.Tag(NormalizeHashtag(token.Value))
                : linkedHandles.Contains(AccountRules.NormalizeHandle(token.Value)) ? SitePaths.Profile(token.Value) : null;
            if (path is null)
            {
                AppendText(html, written);
            }
            else
            {
                var cssClass = token.Kind == BeepTextTokenKind.Hashtag ? "hashtag" : "mention";
                html.Append("<a class=\"").Append(cssClass).Append("\" href=\"").Append(Encoder.Encode(path)).Append("\">");
                AppendText(html, written);
                html.Append("</a>");
            }

            position = token.Start + token.Length;
        }

        AppendText(html, text.AsSpan(position));
        return html.ToString();
    }

    private static ParsedBeepText Rejected(string text, BeepTextProblem problem) => new(text, problem, [], []);

    private static List<string> Hashtags(IReadOnlyList<BeepTextToken> tokens) =>
        [.. tokens.Where(t => t.Kind == BeepTextTokenKind.Hashtag).Select(t => NormalizeHashtag(t.Value)).Distinct(StringComparer.Ordinal)];

    private static void AppendText(StringBuilder html, ReadOnlySpan<char> text)
    {
        var lines = text.ToString().Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                html.Append("<br>");
            }

            html.Append(Encoder.Encode(lines[i]));
        }
    }

    // Exclusions: '&' (HTML entities such as &#39;), '/' (URL fragments such as example.com/#top), '\'.
    private static bool StartsToken(string text, int index)
    {
        if (index == 0)
        {
            return true;
        }

        Rune.DecodeLastFromUtf16(text.AsSpan(0, index), out var previous, out _);
        return !IsWordRune(previous) && previous.Value is not ('&' or '/' or '\\' or '#' or '@');
    }

    private static int HashtagLength(string text, int start)
    {
        var position = start;
        var hasLetter = false;
        while (position < text.Length && Rune.DecodeFromUtf16(text.AsSpan(position), out var rune, out var consumed) == OperationStatus.Done && IsWordRune(rune))
        {
            hasLetter |= Rune.IsLetter(rune);
            position += consumed;
        }

        return hasLetter ? position - start : 0;
    }

    private static int MentionLength(string text, int start)
    {
        var length = AsciiWordLength(text, start);
        if (length == 0)
        {
            return 0;
        }

        var dot = start + length;
        if (dot < text.Length && text[dot] == '.' && AsciiWordLength(text, dot + 1) is > 0 and var nameLength)
        {
            length += 1 + nameLength;
        }

        return length <= Actor.HandleMaxLength ? length : 0;
    }

    private static int AsciiWordLength(string text, int start)
    {
        var position = start;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        return position - start;
    }

    private static bool IsWordRune(Rune rune) =>
        rune.Value == '_'
        || Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}
