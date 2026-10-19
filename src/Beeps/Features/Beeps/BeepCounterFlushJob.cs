using System.Text.Json.Serialization;
using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Beeps;

/// <summary>
/// Writes the live like and reply counts back to the database (PLAN 6.6). It recounts from the like and
/// reply rows instead of copying Valkey, so the columns end up exact even if a live count drifted, and
/// then puts the exact numbers back into Valkey. A count that was corrected goes to open pages too. Views waiting in
/// Valkey are added to their columns in the same run.
/// </summary>
public sealed partial class BeepCounterFlushJob(IBeepCounterStore counters, IBeepStore beeps, LiveUpdates live, ILogger<BeepCounterFlushJob> logger) : IScheduledJob
{
    public const string JobName = "beeps.counters";

    public const int BatchSize = 500;

    public string Name => JobName;

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        IReadOnlyList<Guid> batch;
        do
        {
            batch = await counters.TakeDirtyAsync(BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            try
            {
                var shown = await counters.GetAsync(batch, cancellationToken);
                var counts = await beeps.RecountAsync(batch, cancellationToken);
                await counters.SetAsync(counts, cancellationToken);
                await live.CountsChangedAsync(
                    [.. counts.Where(c => !shown.TryGetValue(c.Key, out var current) || current != c.Value).Select(c => c.Key)],
                    cancellationToken);
            }
            catch
            {
                // Taken from the set already: put them back, or their columns would stay stale.
                await counters.MarkDirtyAsync(batch, CancellationToken.None);
                throw;
            }

            total += batch.Count;
        }
        while (batch.Count >= BatchSize);

        await FlushViewsAsync(cancellationToken);
        if (total > 0)
        {
            LogFlushed(logger, total);
        }
    }

    private async Task FlushViewsAsync(CancellationToken cancellationToken)
    {
        var views = await counters.TakeViewsAsync(cancellationToken);
        try
        {
            await beeps.AddViewsAsync(views, cancellationToken);
        }
        catch
        {
            // Taken already: put them back for the next run.
            await counters.ReturnViewsAsync(views, CancellationToken.None);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote back the counts of {Count} beeps")]
    private static partial void LogFlushed(ILogger logger, int count);
}

/// <summary>
/// A beep whose live count could not be changed (Valkey was away, or it was out of sight), to recount
/// (<see cref="BeepRecountHandler"/>).
/// </summary>
public sealed record BeepRecountRequest(Guid BeepId)
{
    public const string MessageType = "beeps.recount";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BeepRecountRequest))]
internal sealed partial class BeepCountsJsonContext : JsonSerializerContext;

/// <summary>
/// Recounts one beep, as <see cref="BeepCounterFlushJob"/> does, for a change Valkey missed: the column at once, the live
/// count when Valkey is there (until then this throws, and is retried).
/// </summary>
public sealed class BeepRecountHandler(IBeepCounterStore counters, IBeepStore beeps, LiveUpdates live) : IOutboxHandler
{
    public string Type => BeepRecountRequest.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var beepId = message.ReadPayload(BeepCountsJsonContext.Default.BeepRecountRequest).BeepId;
        var counts = await beeps.RecountAsync([beepId], cancellationToken);
        await counters.SetAsync(counts, cancellationToken);
        await live.CountsChangedAsync(beepId, cancellationToken);
    }
}
