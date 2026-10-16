namespace Beeps.Features.Search;

public sealed record SearchRebuildResult(int Actors, int Beeps);

/// <summary>
/// Rebuilds the search indexes from PostgreSQL (PLAN 6.18): the index is derived data and can always be
/// thrown away. Searches keep working on the old indexes until the new ones are complete.
/// </summary>
public sealed partial class SearchIndexer(ISearchIndex index, ISearchStore store, TimeProvider time, ILogger<SearchIndexer> logger)
{
    public const int BatchSize = 1000;

    /// <summary>Beeps posted this long before the rebuild started are copied again afterwards, against clock and commit-order skew.</summary>
    private static readonly TimeSpan CatchUpMargin = TimeSpan.FromMinutes(1);

    public async Task<SearchRebuildResult> RebuildAsync(CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        int actors;
        int beeps;
        await using (var rebuild = await index.StartRebuildAsync(cancellationToken))
        {
            actors = await CopyActorsAsync(rebuild.AddActorsAsync, cancellationToken);
            beeps = await CopyBeepsAsync(null, rebuild.AddBeepsAsync, cancellationToken);
            await rebuild.CompleteAsync(cancellationToken);
        }

        // While the copy ran, sync jobs wrote to the old indexes. Profiles are few, so copy them again; beeps
        // are copied from shortly before the start. Beeps deleted meanwhile may linger in the index, which is
        // harmless: results are always checked against PostgreSQL.
        await CopyActorsAsync(index.UpsertActorsAsync, cancellationToken);
        await CopyBeepsAsync(Guid.CreateVersion7(started - CatchUpMargin), index.UpsertBeepsAsync, cancellationToken);
        LogRebuilt(logger, actors, beeps);
        return new SearchRebuildResult(actors, beeps);
    }

    private async Task<int> CopyActorsAsync(Func<IReadOnlyCollection<ActorDocument>, CancellationToken, Task> add, CancellationToken cancellationToken)
    {
        var total = 0;
        Guid? after = null;
        IReadOnlyList<ActorDocument> batch;
        do
        {
            batch = await store.ListActorsAsync(after, BatchSize, cancellationToken);
            if (batch.Count > 0)
            {
                await add(batch, cancellationToken);
                after = batch[^1].Id;
                total += batch.Count;
            }
        }
        while (batch.Count == BatchSize);
        return total;
    }

    private async Task<int> CopyBeepsAsync(Guid? after, Func<IReadOnlyCollection<BeepDocument>, CancellationToken, Task> add, CancellationToken cancellationToken)
    {
        var total = 0;
        IReadOnlyList<BeepDocument> batch;
        do
        {
            batch = await store.ListBeepsAsync(null, after, BatchSize, cancellationToken);
            if (batch.Count > 0)
            {
                await add(batch, cancellationToken);
                after = batch[^1].Id;
                total += batch.Count;
            }
        }
        while (batch.Count == BatchSize);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Search indexes rebuilt: {Actors} actors, {Beeps} beeps")]
    private static partial void LogRebuilt(ILogger logger, int actors, int beeps);
}
