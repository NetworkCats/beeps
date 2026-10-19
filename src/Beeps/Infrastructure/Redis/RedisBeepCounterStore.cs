using System.Globalization;
using Beeps.Features.Beeps;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

/// <summary>
/// Live counts as one hash per beep (<c>l</c> likes, <c>r</c> replies) and a set of beeps whose counts
/// changed since the last write-back. Every multi-step change is one Lua script.
/// </summary>
public sealed class RedisBeepCounterStore(RedisConnection redis) : IBeepCounterStore
{
    /// <summary>Beeps nobody touched for this long fall back to their columns, which are exact by then.</summary>
    public static readonly TimeSpan CountsLifetime = TimeSpan.FromDays(3);

    public static readonly TimeSpan DirtyLifetime = TimeSpan.FromDays(1);

    /// <summary>
    /// Bound on the write-back set. It only fills up if the job stops; past this, changes still show
    /// live, and their columns catch up at the beep's next like or reply.
    /// </summary>
    public const int MaxDirty = 100_000;

    private static readonly RedisKey DirtyKey = $"{RedisSetup.KeyPrefix}counts:dirty";

    /// <summary>Views waiting to be written back: one field per beep, the number of views.</summary>
    private static readonly RedisKey ViewsKey = $"{RedisSetup.KeyPrefix}views";

    // KEYS: views hash. ARGV: ttl ms, cap, beep id. Returns 0 when the hash is full and the beep not in it yet.
    private const string AddViewScript = """
        if redis.call('HEXISTS', KEYS[1], ARGV[3]) == 0 and redis.call('HLEN', KEYS[1]) >= tonumber(ARGV[2]) then
          return 0
        end
        redis.call('HINCRBY', KEYS[1], ARGV[3], 1)
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return 1
        """;

    // KEYS: views hash. Returns its fields and values, and removes it.
    private const string TakeViewsScript = """
        local all = redis.call('HGETALL', KEYS[1])
        redis.call('DEL', KEYS[1])
        return all
        """;

    // KEYS: views hash. ARGV: ttl ms, then beep id, views for each.
    private const string ReturnViewsScript = """
        for i = 2, #ARGV, 2 do
          redis.call('HINCRBY', KEYS[1], ARGV[i], ARGV[i + 1])
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return 0
        """;

    // KEYS: count hashes. Returns likes, replies for each key in order; '' where there is no hash.
    private const string GetScript = """
        local out = {}
        for i, key in ipairs(KEYS) do
          local v = redis.call('HMGET', key, 'l', 'r')
          out[i * 2 - 1] = v[1] or ''
          out[i * 2] = v[2] or ''
        end
        return out
        """;

    // KEYS: count hash, dirty set. ARGV: base likes, base replies, delta likes, delta replies,
    // counts ttl ms, dirty ttl ms, dirty cap, beep id.
    private const string AddScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then
          redis.call('HSET', KEYS[1], 'l', ARGV[1], 'r', ARGV[2])
        end
        redis.call('HINCRBY', KEYS[1], 'l', ARGV[3])
        redis.call('HINCRBY', KEYS[1], 'r', ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[5])
        if redis.call('SCARD', KEYS[2]) < tonumber(ARGV[7]) then
          redis.call('SADD', KEYS[2], ARGV[8])
        end
        redis.call('PEXPIRE', KEYS[2], ARGV[6])
        return 0
        """;

    // KEYS: count hashes. ARGV: ttl ms, then likes, replies for each key.
    private const string SetScript = """
        for i, key in ipairs(KEYS) do
          redis.call('HSET', key, 'l', ARGV[i * 2], 'r', ARGV[i * 2 + 1])
          redis.call('PEXPIRE', key, ARGV[1])
        end
        return 0
        """;

    public async Task<IReadOnlyDictionary<Guid, BeepCounts>> GetAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        var counts = new Dictionary<Guid, BeepCounts>();
        if (beepIds.Count == 0)
        {
            return counts;
        }

        var ids = beepIds.ToArray();
        var values = (RedisValue[])(await redis.EvaluateAsync(GetScript, [.. ids.Select(CountsKey)]))!;
        for (var i = 0; i < ids.Length; i++)
        {
            if (values[i * 2].TryParse(out int likes) && values[(i * 2) + 1].TryParse(out int replies))
            {
                counts[ids[i]] = new BeepCounts(likes, replies);
            }
        }

        return counts;
    }

    public Task AddAsync(Guid beepId, BeepCounts baseline, BeepCounts delta, CancellationToken cancellationToken) =>
        redis.EvaluateAsync(
            AddScript,
            [CountsKey(beepId), DirtyKey],
            [
                baseline.Likes, baseline.Replies, delta.Likes, delta.Replies,
                RedisCommands.Milliseconds(CountsLifetime), RedisCommands.Milliseconds(DirtyLifetime), MaxDirty, beepId.ToString("D"),
            ]);

    public async Task<bool> AddViewAsync(Guid beepId, CancellationToken cancellationToken) =>
        (int)await redis.EvaluateAsync(AddViewScript, [ViewsKey], [RedisCommands.Milliseconds(DirtyLifetime), MaxDirty, beepId.ToString("D")]) == 1;

    public async Task<IReadOnlyDictionary<Guid, long>> TakeViewsAsync(CancellationToken cancellationToken)
    {
        var values = (RedisValue[])(await redis.EvaluateAsync(TakeViewsScript, [ViewsKey]))!;
        var views = new Dictionary<Guid, long>(values.Length / 2);
        for (var i = 0; i + 1 < values.Length; i += 2)
        {
            if (Guid.TryParse((string?)values[i], CultureInfo.InvariantCulture, out var id) && values[i + 1].TryParse(out long count))
            {
                views[id] = count;
            }
        }

        return views;
    }

    public Task ReturnViewsAsync(IReadOnlyDictionary<Guid, long> views, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(views);
        return views.Count == 0
            ? Task.CompletedTask
            : redis.EvaluateAsync(
                ReturnViewsScript,
                [ViewsKey],
                [RedisCommands.Milliseconds(DirtyLifetime), .. views.SelectMany(v => new RedisValue[] { v.Key.ToString("D"), v.Value })]);
    }

    public Task<IReadOnlyList<Guid>> TakeDirtyAsync(int max, CancellationToken cancellationToken) =>
        redis.PopIdsAsync(DirtyKey, max);

    public Task MarkDirtyAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        return redis.AddIdsCappedAsync(DirtyKey, beepIds, MaxDirty, DirtyLifetime);
    }

    public Task SetAsync(IReadOnlyDictionary<Guid, BeepCounts> counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count == 0)
        {
            return Task.CompletedTask;
        }

        var entries = counts.ToArray();
        return redis.EvaluateAsync(
            SetScript,
            [.. entries.Select(e => CountsKey(e.Key))],
            [RedisCommands.Milliseconds(CountsLifetime), .. entries.SelectMany(e => new RedisValue[] { e.Value.Likes, e.Value.Replies })]);
    }

    private static RedisKey CountsKey(Guid beepId) => $"{RedisSetup.KeyPrefix}counts:{beepId:D}";
}
