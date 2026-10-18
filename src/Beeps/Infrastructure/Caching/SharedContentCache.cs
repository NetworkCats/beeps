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

/// <remarks>The connection is made on first use and waited for asynchronously (<see cref="RedisConnection"/>), so nothing here makes startup wait for Valkey.</remarks>
public sealed class RedisSharedContentCache(RedisConnection redis) : ISharedContentCache
{
    public static readonly RedisChannel InvalidationChannel = RedisChannel.Literal($"{RedisSetup.KeyPrefix}cache-invalidation");

    public async Task<byte[]?[]> GetAsync(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var values = await (await redis.DatabaseAsync()).StringGetAsync([.. keys.Select(k => (RedisKey)k)]);
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
        var batch = (await redis.DatabaseAsync()).CreateBatch();
        var writes = entries.Select(e => batch.StringSetAsync(e.Key, e.Value, expiration)).ToList();
        batch.Execute();
        await Task.WhenAll(writes);
    }

    public async Task DeleteAsync(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count > 0)
        {
            await (await redis.DatabaseAsync()).KeyDeleteAsync([.. keys.Select(k => (RedisKey)k)]);
        }
    }

    public async Task PublishAsync(byte[] message) => await (await redis.GetAsync()).GetSubscriber().PublishAsync(InvalidationChannel, message);

    public async Task SubscribeAsync(Action<byte[]> handler, Action restored)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(restored);
        var connection = await redis.GetAsync();
        await connection.GetSubscriber().SubscribeAsync(InvalidationChannel, (_, message) =>
        {
            if (!message.IsNullOrEmpty)
            {
                handler((byte[])message!);
            }
        });
        // Any reconnect, not only the subscription's: with RESP3 Pub/Sub shares the interactive connection.
        connection.ConnectionRestored += (_, _) => restored();
    }
}
