using Beeps.Infrastructure.Redis;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Caching;

/// <summary>The Valkey side of the content cache: plain strings with a TTL, read and written in batches.</summary>
public interface ISharedContentCache
{
    /// <summary>One value per key, in order; null where there is none.</summary>
    Task<byte[]?[]> GetAsync(IReadOnlyList<string> keys);

    Task SetAsync(IReadOnlyList<KeyValuePair<string, byte[]>> entries, TimeSpan expiration);

    Task DeleteAsync(IReadOnlyCollection<string> keys);

    /// <summary>Tells every instance (this one included) to drop its own copies (PLAN 6.7: Pub/Sub carries cache invalidation).</summary>
    Task PublishAsync(byte[] message);

    /// <summary>Calls <paramref name="handler"/> for every published message, and <paramref name="restored"/> when the connection came back after a drop, during which messages may have been missed.</summary>
    Task SubscribeAsync(Action<byte[]> handler, Action restored);
}

/// <remarks>The connection is taken on first use, so the hosted services holding this never make startup wait for Valkey.</remarks>
public sealed class RedisSharedContentCache(IServiceProvider services) : ISharedContentCache
{
    private IConnectionMultiplexer? connection;

    private IConnectionMultiplexer Redis => connection ??= services.GetRequiredService<IConnectionMultiplexer>();

    public static readonly RedisChannel InvalidationChannel = RedisChannel.Literal($"{RedisSetup.KeyPrefix}cache-invalidation");

    public async Task<byte[]?[]> GetAsync(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var values = await Redis.GetDatabase().StringGetAsync([.. keys.Select(k => (RedisKey)k)]);
        return [.. values.Select(v => v.IsNull ? null : (byte[]?)v)];
    }

    public async Task SetAsync(IReadOnlyList<KeyValuePair<string, byte[]>> entries, TimeSpan expiration)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        // MSET cannot set a TTL; a batch sends every SET EX in one round trip.
        var batch = Redis.GetDatabase().CreateBatch();
        var writes = entries.Select(e => batch.StringSetAsync(e.Key, e.Value, expiration)).ToList();
        batch.Execute();
        await Task.WhenAll(writes);
    }

    public Task DeleteAsync(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Count == 0 ? Task.CompletedTask : Redis.GetDatabase().KeyDeleteAsync([.. keys.Select(k => (RedisKey)k)]);
    }

    public Task PublishAsync(byte[] message) => Redis.GetSubscriber().PublishAsync(InvalidationChannel, message);

    public async Task SubscribeAsync(Action<byte[]> handler, Action restored)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(restored);
        await Redis.GetSubscriber().SubscribeAsync(InvalidationChannel, (_, message) =>
        {
            if (!message.IsNullOrEmpty)
            {
                handler((byte[])message!);
            }
        });
        // Any reconnect, not only the subscription's: with RESP3 Pub/Sub shares the interactive connection.
        Redis.ConnectionRestored += (_, _) => restored();
    }
}
