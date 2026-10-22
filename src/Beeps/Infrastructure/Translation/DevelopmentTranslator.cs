using Beeps.Features.Translation;

namespace Beeps.Infrastructure.Translation;

/// <summary>
/// Local development without an OpenRouter key: takes a beep with any Han character for Chinese and anything else for
/// English, and "translates" it by marking it with the language it went into, so the translated beeps can be tried by
/// hand. A beep containing <see cref="FailMarker"/> fails. Never registered outside development.
/// </summary>
public sealed class DevelopmentTranslator : ITranslator
{
    public const string FailMarker = "[translation:fail]";

    public Task<TranslationVerdict> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains(FailMarker, StringComparison.Ordinal))
        {
            throw new TranslationException("The development translator was asked to fail.");
        }

        var chinese = text.Any(c => c is >= '一' and <= '鿿');
        var verdict = chinese
            ? new TranslationVerdict(TranslationLanguages.Chinese, $"[en] {text}", null, "development", null, 0, null)
            : new TranslationVerdict(TranslationLanguages.English, null, $"[zh] {text}", "development", null, 0, null);
        return Task.FromResult(verdict);
    }
}
