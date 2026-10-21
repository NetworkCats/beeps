using Beeps.Features.Shared;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Jobs;

public sealed class ScheduledJobOptions
{
    public const string SectionName = "ScheduledJobs";

    /// <summary>Whether this instance takes part in running scheduled jobs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often to look for due jobs; each job's own interval decides how often it actually runs.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Checks for due scheduled jobs on every poll. Every instance runs one; the lock and the recorded last run keep jobs from overlapping or repeating.</summary>
public sealed partial class ScheduledJobWorker(
    IServiceScopeFactory scopes,
    IOptions<ScheduledJobOptions> options,
    TimeProvider time,
    ILogger<ScheduledJobWorker> logger) : BackgroundService
{
    private readonly LogThrottle pollFailures = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(settings.PollInterval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ScheduledJobRunner>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Database away and the like: try again at the next poll.
                if (pollFailures.Allow())
                {
                    LogPollFailed(logger, exception);
                }
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled jobs are disabled on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking for due scheduled jobs failed (logged at most once a minute)")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
