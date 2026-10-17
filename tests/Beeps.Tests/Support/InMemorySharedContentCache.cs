using System.Collections.Concurrent;
using Beeps.Infrastructure.Caching;

namespace Beeps.Tests.Support;

/// <summary>The Valkey side of the content cache in memory: records every call, can be made to fail, and delivers published messages to subscribers.</summary>
internal sealed class InMemorySharedContentCache : ISharedContentCache
{
    private readonly List<Action<byte[]>> subscribers = [];

    public ConcurrentDictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, TimeSpan> Expirations { get; } = new(StringComparer.Ordinal);

    public List<IReadOnlyList<string>> Gets { get; } = [];

    public List<IReadOnlyCollection<string>> Deletes { get; } = [];

    public List<byte[]> Published { get; } = [];

    public int Subscribers
    {
        get
        {
            lock (Published)
            {
                return subscribers.Count;
            }
        }
    }

    /// <summary>While set, every call throws it.</summary>
    public Exception? Failure { get; set; }

    public Task<byte[]?[]> GetAsync(IReadOnlyList<string> keys)
    {
        ThrowIfFailing();
        lock (Gets)
        {
            Gets.Add(keys);
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
        lock (Deletes)
        {
            Deletes.Add(keys);
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
        lock (Published)
        {
            Published.Add(message);
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
        lock (Published)
        {
            subscribers.Add(handler);
        }

        return Task.CompletedTask;
    }

    private void ThrowIfFailing()
    {
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}
