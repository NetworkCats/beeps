using Beeps.Features.Shared;

namespace Beeps.Infrastructure.Jobs;

/// <summary>Runs every registered job that is due and not already running on another instance.</summary>
public sealed partial class ScheduledJobRunner(
    IEnumerable<IScheduledJob> jobs,
    IScheduledJobStore store,
    TimeProvider time,
    ILogger<ScheduledJobRunner> logger)
{
    private const int MaxStoredErrorLength = 2000;

    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            await RunIfDueAsync(job, cancellationToken);
        }
    }

    private async Task RunIfDueAsync(IScheduledJob job, CancellationToken cancellationToken)
    {
        await using var lease = await store.TryLockAsync(job.Name, cancellationToken);
        if (lease is null)
        {
            return;
        }

        // Checked under the lock: whoever ran it last, on any instance, decides when it is due again.
        var now = time.GetUtcNow();
        if (await lease.GetLastStartedAsync(cancellationToken) is { } lastStarted && now - lastStarted < job.Interval)
        {
            return;
        }

        await lease.MarkStartedAsync(now, cancellationToken);
        string? error = null;
        try
        {
            await job.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, job.Name);
            error = $"{exception.GetType().Name}: {exception.Message}";
            if (error.Length > MaxStoredErrorLength)
            {
                error = error[..MaxStoredErrorLength];
            }
        }

        await lease.MarkFinishedAsync(time.GetUtcNow(), error, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled job {JobName} failed; it runs again at its next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception, string jobName);
}
