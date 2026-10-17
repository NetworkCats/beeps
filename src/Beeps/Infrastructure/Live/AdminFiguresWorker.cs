using System.Globalization;
using System.Text;
using Beeps.Features.Admin;
using Beeps.Features.Live;
using Beeps.Features.Moderation;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Live;

/// <summary>
/// The admin pages show figures that change with no event: backlogs, dead jobs, scheduled job runs, today's new
/// accounts. While an admin page is open on this instance, this looks at them every few seconds and tells the admin
/// pages (<c>admin_changed</c>) when anything moved, so they are drawn again without the browser asking.
/// </summary>
public sealed partial class AdminFiguresWorker(
    AdminWatchers watchers,
    IServiceScopeFactory scopes,
    LiveUpdates live,
    IOptions<LiveOptions> options,
    TimeProvider time,
    ILogger<AdminFiguresWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        string? last = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Value.AdminFiguresInterval, time, stoppingToken);
                if (!watchers.Any)
                {
                    // Nobody watching: the next one to open a page starts from a fresh look.
                    last = null;
                    continue;
                }

                // The first look after someone started watching counts as a change too: the page may have been rendered
                // before something that this look already finds.
                var figures = await ReadAsync(stoppingToken);
                if (figures != last)
                {
                    await live.AdminChangedAsync(stoppingToken);
                }

                last = figures;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception);
            }
        }
    }

    /// <summary>Everything the admin pages show that no event announces, as one comparable text.</summary>
    private async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var system = scope.ServiceProvider.GetRequiredService<ISystemStore>();
        var moderation = scope.ServiceProvider.GetRequiredService<IModerationStore>();
        var since = new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var counts = await system.CountAsync(since, cancellationToken);
        var queue = await moderation.CountQueueAsync(cancellationToken);
        var dead = (await system.ListDeadJobsAsync(null, 1, cancellationToken)).Items;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{counts}|{queue}|{(dead.Count > 0 ? dead[0].Id : null)}");
        foreach (var job in await system.ListScheduledJobsAsync(cancellationToken))
        {
            text.Append(CultureInfo.InvariantCulture, $"|{job}");
        }

        return text.ToString();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking at the admin figures failed; retrying")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
