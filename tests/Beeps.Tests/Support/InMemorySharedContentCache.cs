using System.Collections.Concurrent;
using System.Text.Json;
using Beeps.Infrastructure.Caching;

namespace Beeps.Tests.Support;

/// <summary>
/// The Valkey side of the content cache in memory: records every call, can be made to fail, and delivers published
/// messages to subscribers. The records are copies, so a test can read them while a worker is still calling.
/// </summary>
internal sealed class InMemorySharedContentCache : ISharedContentCache
{
    private readonly Lock gate = new();
    private readonly List<Action<byte[]>> subscribers = [];
    private readonly List<IReadOnlyList<string>> gets = [];
    private readonly List<IReadOnlyCollection<string>> deletes = [];
    private readonly List<byte[]> published = [];

    public ConcurrentDictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, TimeSpan> Expirations { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<IReadOnlyList<string>> Gets => Snapshot(gets);

    public IReadOnlyList<IReadOnlyCollection<string>> Deletes => Snapshot(deletes);

    public IReadOnlyList<byte[]> Published => Snapshot(published);

    public int Subscribers => Snapshot(subscribers).Length;

    /// <summary>While set, every call throws it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Stores an entry as the content cache would have.</summary>
    public void Put<TKey, TValue>(CacheKind<TKey, TValue> kind, TKey id, TValue value)
        where TKey : notnull =>
        Entries[kind.Key(id)] = JsonSerializer.SerializeToUtf8Bytes(value, kind.Type);

    /// <summary>The first invalidation message published, once there is one.</summary>
    public async Task<CacheInvalidationMessage> PublishedAsync()
    {
        await TestSupport.WaitUntilAsync(() => Published.Count > 0);
        return JsonSerializer.Deserialize(Published[0], ContentCacheJsonContext.Default.CacheInvalidationMessage)!;
    }

    public Task<byte[]?[]> GetAsync(IReadOnlyList<string> keys)
    {
        ThrowIfFailing();
        lock (gate)
        {
            gets.Add(keys);
        }

        return Task.FromResult<byte[]?[]>([.. keys.Select(k => Entries.TryGetValue(k, out var value) ? value : null)]);
    }

    public Task SetAsync(IReadOnlyList<KeyValuePair<string, byte[]>> entries, TimeSpan expiration)
    {
        ThrowIfFailing();
        foreach (var (key, value) in entries)
        {
            Entries[key] = value;
            Expirations[key] = expiration;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> keys)
    {
        ThrowIfFailing();
        lock (gate)
        {
            deletes.Add(keys);
        }

        foreach (var key in keys)
        {
            Entries.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task PublishAsync(byte[] message)
    {
        ThrowIfFailing();
        Action<byte[]>[] handlers;
        lock (gate)
        {
            published.Add(message);
            handlers = [.. subscribers];
        }

        foreach (var handler in handlers)
        {
            handler(message);
        }

        return Task.CompletedTask;
    }

    public Task SubscribeAsync(Action<byte[]> handler, Action restored)
    {
        ThrowIfFailing();
        lock (gate)
        {
            subscribers.Add(handler);
        }

        return Task.CompletedTask;
    }

    private T[] Snapshot<T>(List<T> list)
    {
        lock (gate)
        {
            return [.. list];
        }
    }

    private void ThrowIfFailing()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
