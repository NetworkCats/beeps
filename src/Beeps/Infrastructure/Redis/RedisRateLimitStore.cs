using Beeps.Features.RateLimiting;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

/// <summary>Fixed-window counters in Valkey, checked and incremented in one Lua script.</summary>
public sealed class RedisRateLimitStore(RedisConnection redis) : IRateLimitStore
{
    // KEYS: counter keys. ARGV: limit1, windowMs1, limit2, windowMs2, …, then 1 to count the request or 0 to only ask.
    // Returns 0 when allowed (every counter incremented when counting), otherwise the longest wait in ms
    // (nothing incremented, so rejected requests do not extend the lockout).
    private const string Script = """
        local wait = 0
        for i, key in ipairs(KEYS) do
          local count = tonumber(redis.call('GET', key) or '0')
          if count >= tonumber(ARGV[i * 2 - 1]) then
            local ttl = redis.call('PTTL', key)
            if ttl < 0 then ttl = tonumber(ARGV[i * 2]) end
            if ttl > wait then wait = ttl end
          end
        end
        if wait > 0 or ARGV[#KEYS * 2 + 1] == '0' then return wait end
        for i, key in ipairs(KEYS) do
          if redis.call('INCR', key) == 1 or redis.call('PTTL', key) < 0 then
            redis.call('PEXPIRE', key, ARGV[i * 2])
          end
        end
        return 0
        """;

    // KEYS: counter keys. Never below zero, and a counter whose window ended is left gone.
    private const string ReleaseScript = """
        for _, key in ipairs(KEYS) do
          if tonumber(redis.call('GET', key) or '0') > 0 then redis.call('DECR', key) end
        end
        return 0
        """;

    public async Task<RateLimitDecision> TryAcquireAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counters);
        if (counters.Count == 0)
        {
            return RateLimitDecision.Allowed;
        }

        var waitMs = await EvaluateAsync(counters, count: true);
        return waitMs <= 0 ? RateLimitDecision.Allowed : RateLimitDecision.Denied(TimeSpan.FromMilliseconds(waitMs));
    }

    public async Task<TimeSpan> WaitAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counters);
        return counters.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Max(0, await EvaluateAsync(counters, count: false)));
    }

    public async Task ReleaseAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counters);
        if (counters.Count > 0)
        {
            await redis.EvaluateAsync(ReleaseScript, [.. counters.Select(c => (RedisKey)CounterKey(c))]);
        }
    }

    private static string CounterKey(RateLimitCounter counter) => $"{RedisSetup.KeyPrefix}ratelimit:{counter.Key}";

    private async Task<long> EvaluateAsync(IReadOnlyList<RateLimitCounter> counters, bool count)
    {
        var keys = new RedisKey[counters.Count];
        var values = new RedisValue[(counters.Count * 2) + 1];
        for (var i = 0; i < counters.Count; i++)
        {
            keys[i] = CounterKey(counters[i]);
            values[i * 2] = counters[i].PermitLimit;
            values[(i * 2) + 1] = RedisCommands.Milliseconds(counters[i].Window);
        }

        values[^1] = count ? 1 : 0;
        return (long)await redis.EvaluateAsync(Script, keys, values);
    }
}
