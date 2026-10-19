using System.Buffers;
using System.Globalization;
using System.Text;

namespace Beeps.Features.Shared;

/// <summary>Character rules for free text that others will see (beeps, model names) or that is stored.</summary>
public static class TextRules
{
    /// <summary>
    /// True when the text is not well-formed UTF-16 (a lone surrogate cannot be stored in PostgreSQL) or
    /// holds control characters; line feeds and tabs pass when <paramref name="allowLineBreaks"/> is set.
    /// </summary>
    public static bool HasForbiddenCharacters(string text, bool allowLineBreaks)
    {
        ArgumentNullException.ThrowIfNull(text);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done)
            {
                return true;
            }

            if (Rune.IsControl(rune) && !(allowLineBreaks && (rune.Value == '\n' || rune.Value == '\t')))
            {
                return true;
            }

            remaining = remaining[consumed..];
        }

        return false;
    }

    /// <summary>
    /// Cuts the text to at most <paramref name="maxLength"/> code points, as PostgreSQL counts a column's length,
    /// without splitting a character (a lone surrogate could not be stored) or an emoji sequence.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= maxLength)
        {
            return value;
        }

        var codePoints = 0;
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            codePoints += element.EnumerateRunes().Count();
            if (codePoints > maxLength)
            {
                break;
            }

            end += element.Length;
        }

        return value[..end];
    }
}
