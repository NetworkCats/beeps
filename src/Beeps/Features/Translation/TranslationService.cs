using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Translation;

/// <summary>
/// Translates one beep once it is let through (<see cref="ITranslationJobs"/>): the model finds its language and
/// translates it into English, Chinese or both (<see cref="TranslationLanguages.TargetsFrom"/>). Readers then see the
/// translation their settings ask for (<see cref="ReaderLanguages"/>), with the original a click away.
/// </summary>
public sealed partial class TranslationService(
    ITranslationStore store,
    ITranslator translator,
    IUnitOfWork unitOfWork,
    LiveUpdates live,
    TimeProvider time,
    ILogger<TranslationService> logger)
{
    /// <summary>
    /// Translates the beep if it still needs it: one deleted, hidden, or translated already is left alone. Throws
    /// <see cref="TranslationException"/> when the model gave no usable answer, to be retried.
    /// </summary>
    public async Task TranslateAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var subject = await store.GetSubjectAsync(beepId, cancellationToken);
        if (subject is not { Deleted: false, Translated: false, Status: ModerationStatus.Allowed or ModerationStatus.HumanAllowed } || subject.Text.Length == 0)
        {
            LogSkipped(logger, beepId);
            return;
        }

        var verdict = await translator.TranslateAsync(subject.Text, cancellationToken);
        var translation = ToTranslation(subject, verdict);
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                store.Add(translation);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (translation.English is null && translation.Chinese is null)
        {
            LogNothingToTranslate(logger, beepId, translation.Language);
            return;
        }

        LogTranslated(logger, beepId, translation.Language, verdict.Model, verdict.LatencyMs);
        // Stored already: pages showing the beep redraw it in their reader's language, even if the worker is shutting down.
        await live.BeepsChangedAsync([beepId], CancellationToken.None);
    }

    /// <summary>The translation ran out of attempts: the beep stays as written.</summary>
    public void GiveUp(Guid beepId, int attempts, string error) => LogFailed(logger, beepId, attempts, error);

    private BeepTranslation ToTranslation(TranslationSubject subject, TranslationVerdict verdict)
    {
        var targets = TranslationLanguages.TargetsFrom(verdict.Language);
        return new BeepTranslation
        {
            BeepId = subject.BeepId,
            Language = verdict.Language,
            English = Into(TranslationLanguages.English, verdict.English),
            Chinese = Into(TranslationLanguages.Chinese, verdict.Chinese),
            Model = TextRules.Truncate(verdict.Model, BeepTranslation.ModelMaxLength),
            Provider = verdict.Provider is null ? null : TextRules.Truncate(verdict.Provider, BeepTranslation.ProviderMaxLength),
            LatencyMs = verdict.LatencyMs,
            Tokens = verdict.Tokens,
            CreatedAt = time.GetUtcNow(),
        };

        // Only what the language calls for; one asked for but missing is a bad answer. One the same as the beep shows nothing new.
        string? Into(string target, string? text)
        {
            if (!targets.Contains(target))
            {
                return null;
            }

            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                throw new TranslationException($"The model found the beep to be in \"{verdict.Language}\" but gave no \"{target}\" translation.");
            }

            return trimmed == subject.Text.Trim() ? null : TextRules.Truncate(trimmed, BeepTranslation.TextMaxLength);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Translation of beep {BeepId} skipped: deleted, hidden, translated already, without text, or gone")]
    private static partial void LogSkipped(ILogger logger, Guid beepId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Beep {BeepId} ({Language}) has nothing to translate")]
    private static partial void LogNothingToTranslate(ILogger logger, Guid beepId, string language);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Beep {BeepId} translated from {Language} by {Model} in {LatencyMs} ms")]
    private static partial void LogTranslated(ILogger logger, Guid beepId, string language, string model, int latencyMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Translation of beep {BeepId} failed {Attempts} times and was given up: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid beepId, int attempts, string error);
}
