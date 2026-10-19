using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// Database reads cached in two levels (PLAN 6.6, 6.7): a short-lived copy in this instance, then Valkey,
/// then the loader, which reads PostgreSQL. A whole page of entries takes one Valkey round trip and one load,
/// however many there are (PLAN 12: batch commands, no N+1).
/// </summary>
/// <remarks>
/// <para>
/// Nothing that depends on who reads is cached here; that stays in the database query. Entries are dropped by
/// <see cref="CacheInvalidator"/> after the change that made them stale is committed. Valkey trouble never
/// fails a read: it is logged, Valkey is skipped for a few seconds, and reads go to the database.
/// </para>
/// <para>
/// A missing beep, actor, handle or hashtag is not cached, so creating one never has to invalidate anything. Lists
/// (an owner's agents, whom someone follows) are cached even when empty, so adding to one invalidates it.
/// </para>
/// </remarks>
public sealed partial class ContentCache : IDisposable
{
    private readonly ISharedContentCache shared;
    private readonly IOptions<CacheOptions> options;
    private readonly TimeProvider time;
    private readonly ILogger<ContentCache> logger;
    private readonly MemoryCache local;
    private long sharedPausedUntil;

    public ContentCache(ISharedContentCache shared, IOptions<CacheOptions> options, TimeProvider time, ILogger<ContentCache> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.shared = shared;
        this.options = options;
        this.time = time;
        this.logger = logger;
        local = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.LocalSizeLimitMegabytes * 1024L * 1024L });
    }

    /// <summary>
    /// The entries for <paramref name="ids"/>. Those found in neither level come from <paramref name="load"/>
    /// in one call and are cached; ids it returns nothing for are left out.
    /// </summary>
    public async Task<IReadOnlyDictionary<TKey, TValue>> GetManyAsync<TKey, TValue>(
        CacheKind<TKey, TValue> kind,
        IEnumerable<TKey> ids,
        Func<IReadOnlyList<TKey>, CancellationToken, Task<IReadOnlyDictionary<TKey, TValue>>> load,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(load);
        var wanted = ids.Distinct().ToList();
        var found = new Dictionary<TKey, TValue>(wanted.Count);
        if (wanted.Count == 0)
        {
            return found;
        }

        if (!options.Value.Enabled)
        {
            return await load(wanted, cancellationToken);
        }

        List<TKey> missing = [];
        foreach (var id in wanted)
        {
            if (local.TryGetValue(kind.Key(id), out var value) && value is TValue cached)
            {
                found[id] = cached;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0 && SharedAvailable())
        {
            missing = await ReadSharedAsync(kind, missing, found);
        }

        if (missing.Count == 0)
        {
            return found;
        }

        var loaded = await load(missing, cancellationToken);
        List<KeyValuePair<string, byte[]>> writes = new(loaded.Count);
        foreach (var (id, value) in loaded)
        {
            found[id] = value;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, kind.Type);
            SetLocal(kind.Key(id), value, bytes.Length);
            writes.Add(new(kind.Key(id), bytes));
        }

        if (writes.Count > 0 && SharedAvailable())
        {
            await WriteSharedAsync(writes, kind.Expiration(options.Value));
        }

        return found;
    }

    /// <summary>One entry; see <see cref="GetManyAsync"/>.</summary>
    public async Task<TValue?> GetAsync<TKey, TValue>(
        CacheKind<TKey, TValue> kind,
        TKey id,
        Func<TKey, CancellationToken, Task<TValue?>> load,
        CancellationToken cancellationToken)
        where TKey : notnull
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(load);
        var found = await GetManyAsync<TKey, TValue>(
            kind,
            [id],
            async (missing, ct) => await load(missing[0], ct) is { } value ? new Dictionary<TKey, TValue> { [missing[0]] = value } : new Dictionary<TKey, TValue>(),
            cancellationToken);
        return found.TryGetValue(id, out var result) ? result : default;
    }

    /// <summary>Drops this instance's copies; Valkey and the other instances are <see cref="CacheInvalidator"/>'s job.</summary>
    public void RemoveLocal(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            local.Remove(key);
        }
    }

    /// <summary>Drops every local copy, for when invalidations may have been missed.</summary>
    public void ClearLocal() => local.Clear();

    public void Dispose() => local.Dispose();

    private async Task<List<TKey>> ReadSharedAsync<TKey, TValue>(CacheKind<TKey, TValue> kind, List<TKey> missing, Dictionary<TKey, TValue> found)
        where TKey : notnull
    {
        byte[]?[] values;
        try
        {
            values = await shared.GetAsync([.. missing.Select(kind.Key)]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PauseShared(exception);
            return missing;
        }

        List<TKey> stillMissing = [];
        for (var i = 0; i < missing.Count; i++)
        {
            if (values[i] is { } bytes && TryDeserialize(kind, bytes, out var value))
            {
                found[missing[i]] = value;
                SetLocal(kind.Key(missing[i]), value, bytes.Length);
            }
            else
            {
                stillMissing.Add(missing[i]);
            }
        }

        return stillMissing;
    }

    private async Task WriteSharedAsync(List<KeyValuePair<string, byte[]>> writes, TimeSpan expiration)
    {
        try
        {
            await shared.SetAsync(writes, expiration);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PauseShared(exception);
        }
    }

    private bool TryDeserialize<TKey, TValue>(CacheKind<TKey, TValue> kind, byte[] bytes, out TValue value)
        where TKey : notnull
    {
        try
        {
            value = JsonSerializer.Deserialize(bytes, kind.Type)!;
            return value is not null;
        }
        catch (JsonException exception)
        {
            // Read again from the database, which overwrites the entry.
            LogUnreadable(logger, exception, kind.Name);
            value = default!;
            return false;
        }
    }

    private void SetLocal(string key, object? value, int size) =>
        local.Set(key, value, new MemoryCacheEntryOptions { Size = Math.Max(1, size), AbsoluteExpirationRelativeToNow = options.Value.LocalExpiration });

    private bool SharedAvailable() => time.GetUtcNow().UtcTicks >= Interlocked.Read(ref sharedPausedUntil);

    private void PauseShared(Exception exception)
    {
        var until = (time.GetUtcNow() + options.Value.SharedPauseAfterError).UtcTicks;
        // Logged once per pause, not once per read.
        if (Interlocked.Exchange(ref sharedPausedUntil, until) < time.GetUtcNow().UtcTicks)
        {
            LogSharedUnavailable(logger, exception, options.Value.SharedPauseAfterError);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Valkey is unavailable to the content cache; reading from PostgreSQL for {Pause}")]
    private static partial void LogSharedUnavailable(ILogger logger, Exception exception, TimeSpan pause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A cached {Kind} entry could not be read; loading it again")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string kind);
}
