using Beeps.Features.Shared;

namespace Beeps.Infrastructure.Queues;

/// <summary>
/// Runs one claimed job with retry: failures back off exponentially, and after the last attempt the job is given up
/// on (<see cref="GiveUpAsync"/>). One per job, in its own scope.
/// </summary>
public abstract partial class BeepJobProcessor(IBeepJobStore store, BeepQueueOptions settings, TimeProvider time, ILogger logger)
{
    /// <summary>What the job does, for the logs ("Review", "Translation").</summary>
    protected abstract string Work { get; }

    public async Task ProcessAsync(ClaimedBeepJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        string error;
        if (job.Attempts > settings.MaxAttempts)
        {
            // Only reachable when earlier attempts never finished (the process died mid-job and the lease expired).
            error = "Attempts exhausted without completing; the worker probably crashed during the job.";
        }
        else
        {
            try
            {
                await RunAsync(job.BeepId, cancellationToken);
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
                LogAttemptFailed(logger, exception, Work, job.BeepId, job.Attempts);
                error = FailedAttempts.Describe(exception);
            }
        }

        if (job.Attempts >= settings.MaxAttempts)
        {
            await GiveUpAsync(job.BeepId, error, cancellationToken);
            await store.CompleteAsync(job.Id, cancellationToken);
        }
        else
        {
            await store.RescheduleAsync(job.Id, time.GetUtcNow() + FailedAttempts.RetryDelay(job.Attempts, settings.RetryBaseDelay, settings.RetryMaxDelay), error, cancellationToken);
        }
    }

    /// <summary>Does the job; throws to have it retried.</summary>
    protected abstract Task RunAsync(Guid beepId, CancellationToken cancellationToken);

    /// <summary>The job ran out of attempts and is dropped.</summary>
    protected abstract Task GiveUpAsync(Guid beepId, string error, CancellationToken cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Work} of beep {BeepId} failed on attempt {Attempts}")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, string work, Guid beepId, int attempts);
}
