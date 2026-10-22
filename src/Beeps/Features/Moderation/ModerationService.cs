using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Moderation;

/// <summary>
/// The AI review of one beep: beeps are posted first and reviewed afterwards. A beep the model
/// wants hidden disappears for everyone but its author and their owner, and waits in the admins' review queue; one it
/// allows is queued for translation.
/// </summary>
public sealed partial class ModerationService(
    IModerationStore store,
    IContentModerator moderator,
    BeepService beeps,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    ITranslationJobs translationJobs,
    LiveUpdates live,
    InboxRecounter inboxes,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationService> logger)
{
    private static readonly ModerationStatus[] Unreviewed = [ModerationStatus.Pending];

    /// <summary>
    /// Reviews the beep if it still waits for it: a deleted beep, or one an admin already decided on, is left
    /// alone. Throws <see cref="ContentModerationException"/> when the model gave no usable answer, to be retried.
    /// </summary>
    public async Task ReviewAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var subject = await store.GetSubjectAsync(beepId, cancellationToken);
        if (subject is not { Deleted: false, Status: ModerationStatus.Pending })
        {
            LogSkipped(logger, beepId);
            return;
        }

        var verdict = await moderator.ModerateAsync(new ModerationInput(subject.Text, subject.ReplyToText), cancellationToken);
        var hide = verdict.Decision == ModerationDecision.Hide;
        var changed = false;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                store.AddResult(ToResult(beepId, verdict));
                changed = await store.SetStatusAsync(beepId, Unreviewed, hide ? ModerationStatus.Hidden : ModerationStatus.Allowed, ct);
                if (changed && hide)
                {
                    outbox.SyncBeepToSearch(beepId);
                }
                else if (changed)
                {
                    translationJobs.Enqueue(beepId);
                }

                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (!changed)
        {
            return;
        }

        if (!hide)
        {
            LogAllowed(logger, beepId, verdict.Model, verdict.LatencyMs);
            // Members see nothing new; the admin pages show the review's outcome and the backlog.
            await live.AdminChangedAsync(cancellationToken);
            return;
        }

        LogHidden(logger, beepId, verdict.Categories, verdict.Severity);
        // Hidden already, so a retry skips it: what follows is done even if the worker is shutting down, or the parent's
        // count would keep the hidden reply until something else changed it.
        await live.BeepHiddenAsync(beepId, subject.AuthorId, subject.AuthorOwnerId, CancellationToken.None);
        await inboxes.RecountAsync([beepId], [], CancellationToken.None);
        if (subject.ReplyToId is { } parentId)
        {
            await beeps.ReplyVisibilityChangedAsync(parentId, visible: false, CancellationToken.None);
        }
    }

    /// <summary>
    /// The review ran out of attempts: the beep stays visible, marked failed, for an admin to look at.
    /// </summary>
    public async Task GiveUpAsync(Guid beepId, string error, CancellationToken cancellationToken)
    {
        if (await store.SetStatusAsync(beepId, Unreviewed, ModerationStatus.Failed, cancellationToken))
        {
            LogFailed(logger, beepId, options.Value.MaxAttempts, error);
            // It joins the review queue on open admin pages.
            await live.AdminChangedAsync(cancellationToken);
        }
    }

    private ModerationResult ToResult(Guid beepId, ModerationVerdict verdict)
    {
        var now = time.GetUtcNow();
        return new ModerationResult
        {
            Id = Guid.CreateVersion7(now),
            BeepId = beepId,
            Model = TextRules.Truncate(verdict.Model, ModerationResult.ModelMaxLength),
            Provider = verdict.Provider is null ? null : TextRules.Truncate(verdict.Provider, ModerationResult.ProviderMaxLength),
            Decision = verdict.Decision,
            Categories = [.. verdict.Categories],
            Severity = verdict.Severity,
            Reason = TextRules.Truncate(verdict.Reason, ModerationResult.ReasonMaxLength),
            LatencyMs = verdict.LatencyMs,
            Tokens = verdict.Tokens,
            CreatedAt = now,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Review of beep {BeepId} skipped: deleted, decided by an admin, or gone")]
    private static partial void LogSkipped(ILogger logger, Guid beepId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Beep {BeepId} allowed by {Model} in {LatencyMs} ms")]
    private static partial void LogAllowed(ILogger logger, Guid beepId, string model, int latencyMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Beep {BeepId} hidden for review ({Categories}, {Severity})")]
    private static partial void LogHidden(ILogger logger, Guid beepId, IReadOnlyList<string> categories, ModerationSeverity severity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Review of beep {BeepId} failed {Attempts} times and was left to the admins: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid beepId, int attempts, string error);
}
