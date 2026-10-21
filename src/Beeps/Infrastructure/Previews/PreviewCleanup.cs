using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Previews;

/// <summary>
/// Keeps the folder of previews (<see cref="DiskPreviewStore"/>) to the ones still asked for: once a day, each instance
/// deletes from its own those no one has read in <see cref="PreviewOptions.Retention"/> (a read marks one as used), and
/// any write an instance stopped in the middle of.
/// </summary>
public sealed partial class PreviewCleanup(IOptions<PreviewOptions> options, TimeProvider time, ILogger<PreviewCleanup> logger) : BackgroundService
{
    /// <summary>An image being written, until it is moved into its place.</summary>
    internal const string PartialExtension = ".partial";

    /// <summary>A write takes far less; one this old was cut short.</summary>
    private static readonly TimeSpan PartialAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.CleanupInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, time, stoppingToken);
                var deleted = Sweep();
                LogSwept(logger, deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogSweepFailed(logger, e);
            }
        }
    }

    /// <summary>Deletes what is no longer kept; how many files it deleted.</summary>
    internal int Sweep()
    {
        var root = options.Value.ResolvedDirectory;
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var deleted = 0;
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var age = now - file.LastWriteTimeUtc;
            if (age > (file.Name.EndsWith(PartialExtension, StringComparison.Ordinal) ? PartialAge : options.Value.Retention))
            {
                file.Delete();
                deleted++;
            }
        }

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} link previews no one has asked for lately")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sweeping the link previews failed; trying again at the next sweep")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
