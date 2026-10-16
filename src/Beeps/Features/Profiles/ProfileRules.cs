using Beeps.Features.Accounts;
using Beeps.Features.Shared;

namespace Beeps.Features.Profiles;

/// <summary>
/// What people may write about themselves and their agents (PLAN 2.2): one definition for the settings
/// page, the agent page and the services behind them. Lengths are in code points, as PostgreSQL counts them.
/// </summary>
public static class ProfileRules
{
    /// <summary>Trimmed; null if empty, too long or holding control characters.</summary>
    public static string? CleanDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim();
        return string.IsNullOrEmpty(trimmed)
            || TextRules.HasForbiddenCharacters(trimmed, allowLineBreaks: false)
            || CodePoints(trimmed) > Actor.DisplayNameMaxLength
            ? null
            : trimmed;
    }

    /// <summary>Trimmed, with one kind of line break; an empty bio is no bio (<paramref name="cleaned"/> null).</summary>
    /// <returns>False if the bio is too long or holds control characters.</returns>
    public static bool TryCleanBio(string? bio, out string? cleaned)
    {
        cleaned = null;
        var normalized = (bio ?? "").ReplaceLineEndings("\n").Trim();
        if (normalized.Length == 0)
        {
            return true;
        }

        if (TextRules.HasForbiddenCharacters(normalized, allowLineBreaks: true) || CodePoints(normalized) > Actor.BioMaxLength)
        {
            return false;
        }

        cleaned = normalized;
        return true;
    }

    // Only called on text already checked for lone surrogates, so every rune is a real one.
    private static int CodePoints(string text) => text.EnumerateRunes().Count();
}

/// <summary>Form field names for profile edits; the pages bind inputs with the same names.</summary>
public static class ProfileFields
{
    public const string DisplayName = "DisplayName";
    public const string Bio = "Bio";
}
