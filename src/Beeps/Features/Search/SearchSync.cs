using System.Text.Json.Serialization;
using Beeps.Features.Live;
using Beeps.Features.Shared;

namespace Beeps.Features.Search;

/// <summary>Outbox payload: bring one beep's search document in line with the database.</summary>
public sealed record SearchBeepSyncRequest(Guid BeepId)
{
    public const string MessageType = "search.beep";
}

/// <summary>Outbox payload: bring an actor's (and, for a human, their agents') documents in line; with <see cref="Beeps"/>, their beeps too.</summary>
/// <param name="Beeps">Whether whom may see their beeps changed (ban, unban, deletion).</param>
public sealed record SearchAccountSyncRequest(Guid ActorId, bool Beeps)
{
    public const string MessageType = "search.account";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SearchBeepSyncRequest))]
[JsonSerializable(typeof(SearchAccountSyncRequest))]
internal sealed partial class SearchJsonContext : JsonSerializerContext;

/// <summary>
/// PLAN 12: a change that shows in search results queues its index update in the same transaction. The jobs
/// read the current state when they run, so they are idempotent and the order they run in does not matter.
/// </summary>
public static class SearchSync
{
    public static void SyncBeepToSearch(this IOutbox outbox, Guid beepId)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        outbox.Enqueue(SearchBeepSyncRequest.MessageType, new SearchBeepSyncRequest(beepId), SearchJsonContext.Default.SearchBeepSyncRequest);
    }

    /// <param name="beeps">Also re-index or drop all of their beeps: their visibility changed.</param>
    public static void SyncAccountToSearch(this IOutbox outbox, Guid actorId, bool beeps = false)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        outbox.Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(actorId, beeps), SearchJsonContext.Default.SearchAccountSyncRequest);
    }
}

public sealed class SearchBeepSyncHandler(ISearchStore store, ISearchIndex index, LiveUpdates live) : IOutboxHandler
{
    public string Type => SearchBeepSyncRequest.MessageType;

    public string Queue => OutboxQueues.Search;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.ReadPayload(SearchJsonContext.Default.SearchBeepSyncRequest);
        var indexed = await SyncAsync(request.BeepId, cancellationToken);
        // Another sync of this beep may have run at the same time elsewhere, read the other state, and had its write
        // queued before this one (deleted, then put back by this one): once this write is in, it is checked again.
        if ((await store.FindBeepAsync(request.BeepId, cancellationToken) is not null) != indexed)
        {
            await SyncAsync(request.BeepId, cancellationToken);
        }

        // Open search pages ask again, now that the index has it.
        await live.SearchChangedAsync(cancellationToken);
    }

    /// <summary>Writes what the database has now; whether that put the beep in the index.</summary>
    private async Task<bool> SyncAsync(Guid beepId, CancellationToken cancellationToken)
    {
        if (await store.FindBeepAsync(beepId, cancellationToken) is { } document)
        {
            await index.UpsertBeepsAsync([document], cancellationToken);
            return true;
        }

        await index.DeleteBeepsAsync([beepId], cancellationToken);
        return false;
    }
}

public sealed class SearchAccountSyncHandler(ISearchStore store, ISearchIndex index, LiveUpdates live) : IOutboxHandler
{
    public const int BatchSize = 500;

    public string Type => SearchAccountSyncRequest.MessageType;

    public string Queue => OutboxQueues.Search;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.ReadPayload(SearchJsonContext.Default.SearchAccountSyncRequest);
        var written = await store.GetAccountAsync(request.ActorId, cancellationToken);
        await SyncAsync(request, written, cancellationToken);
        // As for a beep (SearchBeepSyncHandler): a sync at the same time elsewhere may have had its older write queued
        // before this one's (a ban, then the unban's beeps put back), so what changed meanwhile is written again.
        var now = await store.GetAccountAsync(request.ActorId, cancellationToken);
        if (!now.SequenceEqual(written))
        {
            await SyncAsync(request, now, cancellationToken);
        }

        // Open search pages ask again, now that the index has it.
        await live.SearchChangedAsync(cancellationToken);
    }

    private async Task SyncAsync(SearchAccountSyncRequest request, IReadOnlyList<ActorDocumentState> actors, CancellationToken cancellationToken)
    {
        List<ActorDocument> shown = [.. actors.Where(a => a.Document is not null).Select(a => a.Document!)];
        List<Guid> gone = [.. actors.Where(a => a.Document is null).Select(a => a.Id)];
        if (shown.Count > 0)
        {
            await index.UpsertActorsAsync(shown, cancellationToken);
        }

        if (gone.Count > 0)
        {
            await index.DeleteActorsAsync(gone, cancellationToken);
        }

        if (!request.Beeps)
        {
            return;
        }

        if (gone.Count > 0)
        {
            await index.DeleteBeepsByAuthorsAsync(gone, cancellationToken);
        }

        if (shown.Count == 0)
        {
            return;
        }

        IReadOnlyList<Guid> authors = [.. shown.Select(a => a.Id)];
        Guid? after = null;
        IReadOnlyList<BeepDocument> batch;
        do
        {
            batch = await store.ListBeepsAsync(authors, after, BatchSize, cancellationToken);
            if (batch.Count > 0)
            {
                await index.UpsertBeepsAsync(batch, cancellationToken);
                after = batch[^1].Id;
            }
        }
        while (batch.Count == BatchSize);
    }
}
