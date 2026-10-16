using System.Globalization;
using Beeps.Features.RateLimiting;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

/// <summary>Fixed-window counters in Valkey, checked and incremented in one Lua script.</summary>
public sealed class RedisRateLimitStore(IConnectionMultiplexer redis) : IRateLimitStore
{
    // KEYS: counter keys. ARGV: limit1, windowMs1, limit2, windowMs2, …
    // Returns 0 when allowed (every counter incremented), otherwise the longest wait in ms
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
        if wait > 0 then return wait end
        for i, key in ipairs(KEYS) do
          if redis.call('INCR', key) == 1 or redis.call('PTTL', key) < 0 then
            redis.call('PEXPIRE', key, ARGV[i * 2])
          end
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

        var keys = new RedisKey[counters.Count];
        var values = new RedisValue[counters.Count * 2];
        for (var i = 0; i < counters.Count; i++)
        {
            keys[i] = $"{RedisSetup.KeyPrefix}ratelimit:{counters[i].Key}";
            values[i * 2] = counters[i].PermitLimit;
            values[(i * 2) + 1] = ((long)counters[i].Window.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        }

        // StackExchange.Redis has no per-call cancellation; the multiplexer's own timeouts bound the wait.
        var waitMs = (long)await redis.GetDatabase().ScriptEvaluateAsync(Script, keys, values);
        return waitMs <= 0 ? RateLimitDecision.Allowed : RateLimitDecision.Denied(TimeSpan.FromMilliseconds(waitMs));
    }
}
