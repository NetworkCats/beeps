using Beeps.Features.Beeps;

namespace Beeps.Features.Translation;

/// <summary>The model's answer: the beep's language and the translations it asked for, with what the call cost.</summary>
/// <param name="Language">Already normalized (<see cref="TranslationLanguages.Normalize"/>).</param>
/// <param name="English">Empty or null when not asked for.</param>
/// <param name="Chinese">Empty or null when not asked for.</param>
public sealed record TranslationVerdict(
    string Language,
    string? English,
    string? Chinese,
    string Model,
    string? Provider,
    int LatencyMs,
    int? Tokens);

/// <summary>
/// Asks the model to translate one beep. Every failure (network, timeout, 429, 5xx, an answer that does not fit the
/// schema) throws <see cref="TranslationException"/>, so the translation is retried.
/// </summary>
public interface ITranslator
{
    Task<TranslationVerdict> TranslateAsync(string text, CancellationToken cancellationToken);
}

public sealed class TranslationException : Exception
{
    public TranslationException()
    {
    }

    public TranslationException(string message)
        : base(message)
    {
    }

    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Stages a translation on the current unit of work, so it commits with the decision that let the beep through.</summary>
public interface ITranslationJobs
{
    void Enqueue(Guid beepId);
}

/// <summary>A beep as its translation needs it.</summary>
/// <param name="Translated">It has its <see cref="BeepTranslation"/> already.</param>
public sealed record TranslationSubject(Guid BeepId, string Text, ModerationStatus Status, bool Deleted, bool Translated);

public interface ITranslationStore
{
    /// <summary>Any beep, deleted or not; null if there is no such beep.</summary>
    Task<TranslationSubject?> GetSubjectAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>Stages the translation, and drops the beep's cached copy once it commits. Call inside a transaction.</summary>
    void Add(BeepTranslation translation);
}

/// <summary>A beep's translations as stored with it (<see cref="BeepTranslation"/>) and cached.</summary>
public sealed record BeepTranslations(string Language, string? English, string? Chinese)
{
    public string? Into(string language) => language switch
    {
        TranslationLanguages.English => English,
        TranslationLanguages.Chinese => Chinese,
        _ => null,
    };
}

/// <summary>A beep's text in the reader's language, and the language it was written in.</summary>
public sealed record TranslationView(string Language, string Text);

/// <summary>
/// How a signed-in person reads beeps: translated into the language of the site as they use it (<see cref="Target"/>),
/// except those in <see cref="Untranslated"/>, which they read as written.
/// </summary>
/// <param name="Untranslated">Values of <see cref="TranslationLanguages.Choices"/>.</param>
public sealed record ReaderLanguages(string Target, IReadOnlyList<string> Untranslated)
{
    /// <summary>The translation of a beep they are shown, if they read it translated; null to show it as written.</summary>
    public TranslationView? Translate(BeepTranslations? translations)
    {
        if (translations is null
            || translations.Language == Target
            || Untranslated.Contains(TranslationLanguages.ChoiceOf(translations.Language), StringComparer.Ordinal)
            || translations.Into(Target) is not { } text)
        {
            return null;
        }

        return new TranslationView(translations.Language, text);
    }
}
