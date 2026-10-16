using System.Buffers;
using System.Text;

namespace Beeps.Features.Shared;

/// <summary>Character rules for free text that others will see (beeps, model names).</summary>
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
}
