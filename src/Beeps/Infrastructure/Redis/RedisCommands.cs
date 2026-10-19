using System.Globalization;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

/// <summary>The commands the Valkey stores share.</summary>
internal static class RedisCommands
{
    // KEYS: set. ARGV: ttl ms, cap, members… Checked per member, so a batch stops at the cap too.
    private const string AddCappedScript = """
        for i = 3, #ARGV do
          if redis.call('SCARD', KEYS[1]) < tonumber(ARGV[2]) then
            redis.call('SADD', KEYS[1], ARGV[i])
          end
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return 0
        """;

    /// <summary>A lifetime as the whole milliseconds <c>PEXPIRE</c> takes.</summary>
    public static RedisValue Milliseconds(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

    // StackExchange.Redis has no per-call cancellation; the multiplexer's own timeouts bound the wait.
    public static async Task<RedisResult> EvaluateAsync(this RedisConnection redis, string script, RedisKey[] keys, RedisValue[]? values = null) =>
        await (await redis.DatabaseAsync()).ScriptEvaluateAsync(script, keys, values);

    /// <summary>Adds the ids to a set of at most <paramref name="cap"/> members, past which they are dropped, and renews its lifetime.</summary>
    public static Task AddIdsCappedAsync(this RedisConnection redis, RedisKey key, IReadOnlyCollection<Guid> ids, int cap, TimeSpan lifetime) =>
        ids.Count == 0
            ? Task.CompletedTask
            : redis.EvaluateAsync(AddCappedScript, [key], [Milliseconds(lifetime), cap, .. ids.Select(id => (RedisValue)id.ToString("D"))]);

    /// <summary>Removes and returns up to <paramref name="max"/> ids from the set, skipping any member that is not one.</summary>
    public static async Task<IReadOnlyList<Guid>> PopIdsAsync(this RedisConnection redis, RedisKey key, int max)
    {
        var members = await (await redis.DatabaseAsync()).SetPopAsync(key, max);
        return [.. members.Select(m => Guid.TryParse((string?)m, CultureInfo.InvariantCulture, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
    }
}
