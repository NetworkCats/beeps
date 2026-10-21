using Beeps.Features.Moderation;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Runs one claimed review with retry: failures back off exponentially, and after the last attempt
/// the beep is left visible, marked failed, for the admins. One per review, in its own scope.
/// </summary>
public sealed partial class ModerationProcessor(
    IModerationJobStore store,
    ModerationService moderation,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationProcessor> logger)
{
    public async Task ProcessAsync(ClaimedModerationJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var settings = options.Value;
        string error;
        if (job.Attempts > settings.MaxAttempts)
        {
            // Only reachable when earlier attempts never finished (the process died mid-review and the lease expired).
            error = "Attempts exhausted without completing; the worker probably crashed during the review.";
        }
        else
        {
            try
            {
                await moderation.ReviewAsync(job.BeepId, cancellationToken);
                await store.CompleteAsync(job.Id, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down: leave it claimed; the lease expires and another worker retries it.
                throw;
            }
            catch (Exception exception)
            {
                LogAttemptFailed(logger, exception, job.BeepId, job.Attempts);
                error = FailedAttempts.Describe(exception);
            }
        }

        if (job.Attempts >= settings.MaxAttempts)
        {
            await moderation.GiveUpAsync(job.BeepId, error, cancellationToken);
            await store.CompleteAsync(job.Id, cancellationToken);
        }
        else
        {
            await store.RescheduleAsync(job.Id, time.GetUtcNow() + FailedAttempts.RetryDelay(job.Attempts, settings.RetryBaseDelay, settings.RetryMaxDelay), error, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Review of beep {BeepId} failed on attempt {Attempts}")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, Guid beepId, int attempts);
}
