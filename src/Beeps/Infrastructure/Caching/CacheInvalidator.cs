using System.Text.Json;
using System.Threading.Channels;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Data.Stores;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// Drops cached copies of committed changes, off the request (PLAN 6.7). The request only removes this
/// instance's copies of the entries and queues the change; this worker then works out the affected output-cached
/// pages, deletes the entries from Valkey and publishes both on Pub/Sub, which every instance (this one too)
/// applies to its own copies (<see cref="CacheInvalidationListener"/>).
/// </summary>
/// <remarks>
/// <para>
/// Only what the change touched is dropped: a beep's entry and the pages that can show it, an actor's entry and
/// its profile. Only a lifted ban evicts every cached page: the account's beeps come back on pages that never showed them.
/// </para>
/// <para>
/// Each batch runs a second time <see cref="CacheOptions.SecondPassDelay"/> later: a read that loaded the old row
/// just before the commit may write it back after the first pass. Failed passes are retried; the lifetimes in
/// <see cref="CacheOptions"/> bound what is left if an instance stops before it gets to them.
/// </para>
/// </remarks>
public sealed partial class CacheInvalidator(
    ContentCache cache,
    ISharedContentCache shared,
    IServiceScopeFactory scopes,
    IOutputCacheStore pages,
    HybridCache hybrid,
    IOptions<CacheOptions> options,
    TimeProvider time,
    ILogger<CacheInvalidator> logger) : BackgroundService
{
    /// <summary>Changes held while Valkey is away; past this, the oldest wait for their entries to expire instead.</summary>
    public const int MaxPending = 100_000;

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Channel<Pass> queue = Channel.CreateUnbounded<Pass>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Called after the commit; returns at once.</summary>
    public void Invalidate(IReadOnlyCollection<CacheChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        // At once, so this instance's next read (often the redirect after the change) already misses.
        cache.RemoveLocal(changes.SelectMany(KeysOf));
        queue.Writer.TryWrite(new Pass([.. changes], null));
    }

    /// <summary>
    /// Drops entries that decide who a request is (<see cref="SharedCache"/>) after the change behind them committed:
    /// at once, so the change applies to the very next request, and again <see cref="CacheOptions.SecondPassDelay"/>
    /// later, as a request that read the old row just before the change may have written it back in between. Never
    /// fails the caller, whose change is committed already: if Valkey is away, the removal is retried here until it is
    /// back, and until then every read of these entries goes to the database anyway. The retry is also written to the
    /// outbox (<see cref="SharedCacheForgetHandler"/>), so a restart before Valkey is back does not leave a revoked
    /// session or key cached.
    /// </summary>
    public async Task ForgetSharedAsync(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return;
        }

        var pass = new Pass([], new Resolved([], [], [.. keys]));
        try
        {
            // Not the request's token: a client that goes away must not leave a revoked session cached.
            await hybrid.RemoveAsync(keys, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogForgetFailed(logger, exception, keys.Count);
            queue.Writer.TryWrite(pass);
            await StoreForgetAsync(keys);
        }

        QueueLater(pass, CancellationToken.None);
    }

    /// <summary>
    /// Runs one batch to the end, now: the outbox's durable copy of a commit (<see cref="CacheInvalidationHandler"/>).
    /// Throws when Valkey is away, so the outbox retries it.
    /// </summary>
    public async Task RunAsync(IReadOnlyList<CacheChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        await PublishAsync(await ResolveAsync([new Pass(changes, null)], cancellationToken), cancellationToken);
    }

    /// <summary>What a message asks of this instance: drop the entries and evict the pages.</summary>
    public static async Task ApplyLocallyAsync(ContentCache cache, IOutputCacheStore pages, CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(message);
        cache.RemoveLocal(message.Keys);
        foreach (var tag in message.PageTags)
        {
            await pages.EvictByTagAsync(tag, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<Pass> batch = [];
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (batch.Count == 0)
                {
                    batch.Add(await queue.Reader.ReadAsync(stoppingToken));
                }

                while (queue.Reader.TryRead(out var more))
                {
                    batch.Add(more);
                }

                batch = Bound(batch);
                var resolved = await ResolveAsync(batch, stoppingToken);
                await PublishAsync(resolved, stoppingToken);
                ScheduleSecondPass(batch, resolved, stoppingToken);
                batch = [];
                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Keep the batch (more changes join it) and try again.
                failures++;
                var delay = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, Math.Pow(2, failures - 1)));
                LogFailed(logger, exception, batch.Sum(p => p.Changes.Count), delay);
                try
                {
                    await Task.Delay(delay, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task StoreForgetAsync(IReadOnlyCollection<string> keys)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IOutbox>()
                .Enqueue(SharedCacheForgetRequest.MessageType, new SharedCacheForgetRequest([.. keys]), ContentCacheJsonContext.Default.SharedCacheForgetRequest);
            await scope.ServiceProvider.GetRequiredService<BeepsDbContext>().SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The in-process retry is still queued.
            LogForgetNotStored(logger, exception, keys.Count);
        }
    }

    private static IEnumerable<string> KeysOf(CacheChange change) =>
        change.Kind switch
        {
            CacheChangeKind.Beep or CacheChangeKind.BeepRemoved => [CacheKinds.Beep.Key(change.Id)],
            CacheChangeKind.Actor or CacheChangeKind.AgentConnected or CacheChangeKind.ActorVisibility or CacheChangeKind.ActorRestored => [CacheKinds.Actor.Key(change.Id)],
            CacheChangeKind.Handle => [CacheKinds.Handle.Key(change.Name!)],
            CacheChangeKind.Following => [CacheKinds.Following.Key(change.Id)],
            CacheChangeKind.Agents => [CacheKinds.Agents.Key(change.Id)],
            _ => [],
        };

    /// <summary>The cached pages a change affects that are known without reading anything (<see cref="PageTags"/>).</summary>
    private static IEnumerable<string> PageTagsOf(CacheChange change) =>
        change.Kind switch
        {
            // Pages showing the beep: its images, its counts, or whether it is there at all changed.
            CacheChangeKind.Beep or CacheChangeKind.BeepRemoved or CacheChangeKind.BeepHidden or CacheChangeKind.BeepCounts => [PageTags.Beep(change.Id)],
            // Pages showing the actor, wherever: its beeps, mentions of it, its profile, its owner's or agents' profiles.
            CacheChangeKind.Actor or CacheChangeKind.ActorVisibility => [PageTags.Shows(change.Id)],
            // Its beeps come back, on pages that never showed them.
            CacheChangeKind.ActorRestored => [PageTags.All],
            _ => [],
        };

    private List<Pass> Bound(List<Pass> batch)
    {
        var total = batch.Sum(p => p.Changes.Count);
        if (total <= MaxPending)
        {
            return batch;
        }

        LogDropped(logger, total - MaxPending);
        var kept = new List<Pass>();
        var room = MaxPending;
        for (var i = batch.Count - 1; i >= 0 && room > 0; i--)
        {
            kept.Insert(0, batch[i]);
            room -= batch[i].Changes.Count;
        }

        return kept;
    }

    /// <summary>The entries and pages a batch affects; a second pass reuses what its first pass found.</summary>
    private async Task<Resolved> ResolveAsync(List<Pass> batch, CancellationToken cancellationToken)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        HashSet<string> tags = new(StringComparer.Ordinal);
        HashSet<string> hybridKeys = new(StringComparer.Ordinal);
        List<CacheChange> unresolved = [];
        foreach (var pass in batch)
        {
            if (pass.Resolved is { } done)
            {
                keys.UnionWith(done.Keys);
                tags.UnionWith(done.PageTags);
                hybridKeys.UnionWith(done.HybridKeys);
            }
            else
            {
                unresolved.AddRange(pass.Changes);
            }
        }

        foreach (var change in unresolved)
        {
            keys.UnionWith(KeysOf(change));
            if (change.Kind is CacheChangeKind.Actor or CacheChangeKind.AgentConnected or CacheChangeKind.ActorVisibility or CacheChangeKind.ActorRestored)
            {
                // A human's navigation bar shows their name and avatar from the cached account state.
                hybridKeys.Add(SharedCache.AccountKey(change.Id));
            }
        }

        foreach (var change in unresolved)
        {
            tags.UnionWith(PageTagsOf(change));
        }

        if (!tags.Contains(PageTags.All) && unresolved.Any(c => c.Kind is CacheChangeKind.BeepPosted or CacheChangeKind.ActorVisibility or CacheChangeKind.AgentConnected))
        {
            await using var scope = scopes.CreateAsyncScope();
            var reads = scope.ServiceProvider.GetRequiredService<ContentReads>();
            tags.UnionWith(await NewBeepPageTagsAsync(reads, unresolved, cancellationToken));
            tags.UnionWith(await HiddenAgentPageTagsAsync(reads, unresolved, cancellationToken));
            tags.UnionWith(await ConnectedAgentPageTagsAsync(reads, unresolved, cancellationToken));
        }

        return new Resolved([.. keys], [.. tags], [.. hybridKeys]);
    }

    /// <summary>Where a new beep belongs on a cached page: its thread, its author's (and owner's) profile, its hashtags, the public timeline.</summary>
    private static async Task<IEnumerable<string>> NewBeepPageTagsAsync(ContentReads reads, List<CacheChange> changes, CancellationToken cancellationToken)
    {
        var ids = changes.Where(c => c.Kind == CacheChangeKind.BeepPosted).Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var beeps = await reads.GetBeepsAsync(ids, cancellationToken);
        var authors = await reads.GetActorsAsync(beeps.Values.Select(b => b.AuthorId), cancellationToken);
        return beeps.Values.SelectMany(beep =>
        {
            List<string> tags = [PageTags.Thread(beep.RootId), PageTags.Actor(beep.AuthorId), .. BeepText.Hashtags(beep.Text).Select(PageTags.Hashtag)];
            if (authors.TryGetValue(beep.AuthorId, out var author) && author.Actor.OwnerId is { } owner)
            {
                tags.Add(PageTags.Actor(owner));
            }

            if (beep.ReplyToId is null)
            {
                tags.Add(PageTags.Public);
            }

            return tags;
        });
    }

    /// <summary>A newly connected agent appears on its owner's profile, which did not show it before.</summary>
    private static async Task<IEnumerable<string>> ConnectedAgentPageTagsAsync(ContentReads reads, List<CacheChange> changes, CancellationToken cancellationToken)
    {
        var ids = changes.Where(c => c.Kind == CacheChangeKind.AgentConnected).Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var agents = await reads.GetActorsAsync(ids, cancellationToken);
        return agents.Values.Select(a => a.Actor.OwnerId).OfType<Guid>().Select(PageTags.Actor);
    }

    /// <summary>A banned or deleted human's agents disappear with them, without changes of their own.</summary>
    private static async Task<IEnumerable<string>> HiddenAgentPageTagsAsync(ContentReads reads, List<CacheChange> changes, CancellationToken cancellationToken)
    {
        var ids = changes.Where(c => c.Kind == CacheChangeKind.ActorVisibility).Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        // An agent has none, so its list is empty.
        var agents = await reads.GetAgentIdsAsync(ids, cancellationToken);
        return agents.Values.SelectMany(a => a).Select(PageTags.Shows);
    }

    private async Task PublishAsync(Resolved resolved, CancellationToken cancellationToken)
    {
        var message = new CacheInvalidationMessage(resolved.Keys, resolved.PageTags);
        // Here first: this instance is right even while Valkey is away.
        await ApplyLocallyAsync(cache, pages, message, cancellationToken);
        if (resolved.HybridKeys.Count > 0)
        {
            await hybrid.RemoveAsync(resolved.HybridKeys, cancellationToken);
        }

        if (resolved.Keys.Count == 0 && resolved.PageTags.Count == 0)
        {
            // Nothing any instance holds a copy of (ForgetSharedAsync).
            return;
        }

        await shared.DeleteAsync(resolved.Keys);
        await shared.PublishAsync(JsonSerializer.SerializeToUtf8Bytes(message, ContentCacheJsonContext.Default.CacheInvalidationMessage));
    }

    private void ScheduleSecondPass(List<Pass> batch, Resolved resolved, CancellationToken stoppingToken)
    {
        // Counts are read live from Valkey, which changed before the pages were evicted: no read can have put old
        // ones back, and the counts worker sends them every few seconds.
        if (batch.All(p => p.Resolved is not null || p.Changes.All(c => c.Kind == CacheChangeKind.BeepCounts)))
        {
            return;
        }

        var changes = batch.Where(p => p.Resolved is null).SelectMany(p => p.Changes).ToList();
        QueueLater(new Pass(changes, resolved), stoppingToken);
    }

    private void QueueLater(Pass pass, CancellationToken stoppingToken) =>
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(options.Value.SecondPassDelay, time, stoppingToken);
                    queue.Writer.TryWrite(pass);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down.
                }
            },
            CancellationToken.None);

    /// <param name="Resolved">Set on a second pass: the entries and pages its first pass found.</param>
    private sealed record Pass(IReadOnlyList<CacheChange> Changes, Resolved? Resolved);

    /// <param name="HybridKeys">Entries of <see cref="SharedCache"/>, which live in Valkey only.</param>
    private sealed record Resolved(IReadOnlyList<string> Keys, IReadOnlyList<string> PageTags, IReadOnlyList<string> HybridKeys);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalidating {Count} cache changes failed; retrying in {Delay}")]
    private static partial void LogFailed(ILogger logger, Exception exception, int count, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropping {Count} cached sign-in entries failed; retrying in the background")]
    private static partial void LogForgetFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Storing the retry of {Count} cached sign-in entries failed; it is retried in this process only")]
    private static partial void LogForgetNotStored(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} cache invalidations were dropped while Valkey was away; those entries stay until they expire")]
    private static partial void LogDropped(ILogger logger, int count);
}
