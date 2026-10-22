using Beeps.Features.Beeps;

namespace Beeps.Features.Translation;

/// <summary>
/// A beep waiting to be translated, in its own queue, as reviews are (<see cref="Moderation.ModerationJob"/>). Queued
/// once the beep is let through: by the model's review, an admin's, or at once when there is no review.
/// <see cref="RunAfter"/> doubles as the claim lease while a worker holds it.
/// </summary>
public sealed class TranslationJob
{
    public Guid Id { get; init; }

    public Guid BeepId { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset RunAfter { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string? LastError { get; init; }
}

/// <summary>
/// A beep's language and its translations (<see cref="TranslationLanguages.TargetsFrom"/>): English into Chinese,
/// Chinese into English, anything else into both. One per beep, also when there was nothing to translate, so it is
/// never asked for again.
/// </summary>
public sealed class BeepTranslation
{
    public const int LanguageMaxLength = 8;
    public const int ModelMaxLength = 100;
    public const int ProviderMaxLength = 100;

    /// <summary>Translations run longer than what they translate; Chinese into English most.</summary>
    public const int TextMaxLength = BeepText.MaxLength * 4;

    public Guid BeepId { get; init; }

    /// <summary>The language the beep is written in, as the model found it (<see cref="TranslationLanguages.Normalize"/>).</summary>
    public required string Language { get; init; }

    public string? English { get; init; }

    /// <summary>In Simplified Chinese.</summary>
    public string? Chinese { get; init; }

    /// <summary>The model that answered, as OpenRouter reports it.</summary>
    public required string Model { get; init; }

    public string? Provider { get; init; }

    public int LatencyMs { get; init; }

    public int? Tokens { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Language codes: ISO 639-1 where there is one, as the model and the settings use them.</summary>
public static class TranslationLanguages
{
    public const string English = "en";
    public const string Chinese = "zh";

    /// <summary>ISO 639-2's "undetermined": nothing to translate (links, mentions, code or emoji alone).</summary>
    public const string Undetermined = "und";

    /// <summary>In the settings, every language not offered by name.</summary>
    public const string Other = "other";

    /// <summary>The languages the settings offer by name, after the two the site is in.</summary>
    public static readonly IReadOnlyList<string> Named = [English, Chinese, "ja", "ko", "es", "fr", "de", "pt", "ru", "it", "ar", "hi"];

    /// <summary>What the settings switch: <see cref="Named"/>, then <see cref="Other"/>.</summary>
    public static readonly IReadOnlyList<string> Choices = [.. Named, Other];

    /// <summary>The language a reader of the site in <paramref name="culture"/> gets translations into.</summary>
    public static string TargetFor(string culture) =>
        culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;

    /// <summary>What a beep in <paramref name="language"/> is translated into.</summary>
    public static IReadOnlyList<string> TargetsFrom(string language) => language switch
    {
        English => [Chinese],
        Chinese => [English],
        Undetermined => [],
        _ => [English, Chinese],
    };

    /// <summary>A language code as stored: lower case, without region or script (<c>zh-Hant</c> is <c>zh</c>); null if it is none.</summary>
    public static string? Normalize(string? code)
    {
        var primary = code?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary is { Length: >= 2 and <= 3 } && primary.All(char.IsAsciiLetterLower) ? primary : null;
    }

    /// <summary>The settings' switch for a language.</summary>
    public static string ChoiceOf(string language) => Named.Contains(language, StringComparer.Ordinal) ? language : Other;

    /// <summary>
    /// The languages left untranslated once the settings' switches are sent: those not in <paramref name="translated"/>.
    /// The reader's own (<paramref name="target"/>) has no switch, so it keeps what it had.
    /// </summary>
    public static List<string> Untranslated(IReadOnlyCollection<string> before, IReadOnlyCollection<string> translated, string target) =>
        [.. Choices.Where(l => l == target ? before.Contains(l) : !translated.Contains(l))];
}
