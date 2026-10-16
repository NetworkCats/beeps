using System.Globalization;
using Beeps.Features.Live;
using Beeps.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Live;

/// <summary>
/// One Valkey stream for all live events (PLAN 6.7: Stream, so a reconnecting browser can replay), capped in
/// length; a set of beeps whose counts changed; and a sorted set per account of its open connections.
/// </summary>
public sealed class RedisLiveEventStore(IConnectionMultiplexer redis, IOptions<LiveOptions> options) : ILiveEventStore
{
    /// <summary>Every key here is rewritten constantly while the site is in use; the lifetime only matters once it is not.</summary>
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(1);

    /// <summary>Bound on the changed-counts set; it only fills up if every instance stops publishing.</summary>
    public const int MaxChangedCounts = 100_000;

    private const string TypeField = "t";
    private const string DataField = "d";

    private static readonly RedisKey StreamKey = $"{RedisSetup.KeyPrefix}events";
    private static readonly RedisKey ChangedCountsKey = $"{RedisSetup.KeyPrefix}events:counts";

    // KEYS: stream. ARGV: max length, ttl ms, type, data. MAXLEN ~ lets Valkey trim whole nodes, which is cheap.
    private const string AppendScript = """
        redis.call('XADD', KEYS[1], 'MAXLEN', '~', ARGV[1], '*', 't', ARGV[3], 'd', ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return 0
        """;

    // KEYS: set. ARGV: ttl ms, cap, beep id.
    private const string MarkScript = """
        if redis.call('SCARD', KEYS[1]) < tonumber(ARGV[2]) then
          redis.call('SADD', KEYS[1], ARGV[3])
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return 0
        """;

    // KEYS: the account's connections. ARGV: connection id, now ms, max, ttl ms. Returns the ids pushed out, oldest first.
    private const string AddConnectionScript = """
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[1])
        local extra = redis.call('ZCARD', KEYS[1]) - tonumber(ARGV[3])
        local evicted = {}
        if extra > 0 then
          evicted = redis.call('ZRANGE', KEYS[1], 0, extra - 1)
          redis.call('ZREMRANGEBYRANK', KEYS[1], 0, extra - 1)
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[4])
        return evicted
        """;

    public Task AppendAsync(string type, string data, CancellationToken cancellationToken) =>
        redis.GetDatabase().ScriptEvaluateAsync(AppendScript, [StreamKey], [options.Value.StreamMaxLength, Milliseconds(KeyLifetime), type, data]);

    public async Task<IReadOnlyList<LiveEvent>> ReadAfterAsync(LiveEventId after, int max, CancellationToken cancellationToken)
    {
        var entries = await redis.GetDatabase().StreamReadAsync(StreamKey, after.ToString(), max);
        return [.. entries.Select(ToEvent)];
    }

    public async Task<LiveEventId?> TrimmedUpToAsync(CancellationToken cancellationToken)
    {
        StreamInfo info;
        try
        {
            info = await redis.GetDatabase().StreamInfoAsync(StreamKey);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return LiveEventId.TryParse((string?)info.MaxDeletedEntryId, out var trimmed) && trimmed > LiveEventId.Zero ? trimmed : null;
    }

    public async Task<LiveEventId?> LastIdAsync(CancellationToken cancellationToken) =>
        Id(await redis.GetDatabase().StreamRangeAsync(StreamKey, "-", "+", 1, Order.Descending));

    public Task MarkCountsChangedAsync(Guid beepId, CancellationToken cancellationToken) =>
        redis.GetDatabase().ScriptEvaluateAsync(MarkScript, [ChangedCountsKey], [Milliseconds(KeyLifetime), MaxChangedCounts, beepId.ToString("D")]);

    public async Task<IReadOnlyList<Guid>> TakeChangedCountsAsync(int max, CancellationToken cancellationToken)
    {
        var members = await redis.GetDatabase().SetPopAsync(ChangedCountsKey, max);
        return [.. members.Select(m => Guid.TryParse((string?)m, CultureInfo.InvariantCulture, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
    }

    public async Task<IReadOnlyList<string>> AddConnectionAsync(Guid accountId, string connectionId, DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        var evicted = (RedisValue[])(await redis.GetDatabase().ScriptEvaluateAsync(
            AddConnectionScript,
            [ConnectionsKey(accountId)],
            [connectionId, now.ToUnixTimeMilliseconds(), max, Milliseconds(KeyLifetime)]))!;
        return [.. evicted.Select(e => (string)e!)];
    }

    public Task RemoveConnectionAsync(Guid accountId, string connectionId, CancellationToken cancellationToken) =>
        redis.GetDatabase().SortedSetRemoveAsync(ConnectionsKey(accountId), connectionId);

    private static LiveEvent ToEvent(StreamEntry entry) =>
        new(entry.Id!, (string?)entry[TypeField] ?? "", (string?)entry[DataField] ?? "");

    private static LiveEventId? Id(StreamEntry[] entries) =>
        entries.Length == 0 ? null : LiveEventId.Parse(entries[0].Id!);

    private static RedisKey ConnectionsKey(Guid accountId) => $"{RedisSetup.KeyPrefix}sse:{accountId:D}";

    private static RedisValue Milliseconds(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
