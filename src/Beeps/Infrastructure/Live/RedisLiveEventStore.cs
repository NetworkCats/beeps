using System.Globalization;
using Beeps.Features.Live;
using Beeps.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Live;

/// <summary>
/// One Valkey stream for all live events (PLAN 6.7: Stream, so a reconnecting browser can replay), capped in
/// length, with a Pub/Sub message per event so readers need not poll it; a set of beeps whose counts changed; and a
/// sorted set per account of its open connections.
/// </summary>
public sealed class RedisLiveEventStore(RedisConnection redis, IOptions<LiveOptions> options) : ILiveEventStore
{
    /// <summary>Every key here is rewritten constantly while the site is in use; the lifetime only matters once it is not.</summary>
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(1);

    /// <summary>Bound on the changed-counts set; it only fills up if every instance stops publishing.</summary>
    public const int MaxChangedCounts = 100_000;

    private const string TypeField = "t";
    private const string DataField = "d";

    private static readonly RedisKey StreamKey = $"{RedisSetup.KeyPrefix}events";
    private static readonly RedisKey ChangedCountsKey = $"{RedisSetup.KeyPrefix}events:counts";
    private static readonly RedisChannel AppendedChannel = RedisChannel.Literal($"{RedisSetup.KeyPrefix}events:appended");

    // KEYS: stream. ARGV: max length, ttl ms, type, data, channel. MAXLEN ~ lets Valkey trim whole nodes, which is cheap.
    // Published after the entry is added, so whoever hears it finds the entry.
    private const string AppendScript = """
        redis.call('XADD', KEYS[1], 'MAXLEN', '~', ARGV[1], '*', 't', ARGV[3], 'd', ARGV[4])
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        redis.call('PUBLISH', ARGV[5], '')
        return 0
        """;

    // KEYS: set. ARGV: ttl ms, cap, beep ids.
    private const string MarkScript = """
        if redis.call('SCARD', KEYS[1]) < tonumber(ARGV[2]) then
          redis.call('SADD', KEYS[1], unpack(ARGV, 3))
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
        EvaluateAsync(
            AppendScript,
            [StreamKey],
            [options.Value.StreamMaxLength, Milliseconds(KeyLifetime), type, data, AppendedChannel.ToString()]);

    public async Task SubscribeAppendedAsync(Action appended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appended);
        await (await redis.GetAsync()).GetSubscriber().SubscribeAsync(AppendedChannel, (_, _) => appended());
    }

    public async Task<IReadOnlyList<LiveEvent>> ReadAfterAsync(LiveEventId after, int max, CancellationToken cancellationToken)
    {
        var entries = await (await redis.DatabaseAsync()).StreamReadAsync(StreamKey, after.ToString(), max);
        return [.. entries.Select(ToEvent)];
    }

    public async Task<LiveEventId?> TrimmedUpToAsync(CancellationToken cancellationToken)
    {
        StreamInfo info;
        try
        {
            info = await (await redis.DatabaseAsync()).StreamInfoAsync(StreamKey);
        }
        catch (RedisServerException exception) when (exception.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return TrimmedUpTo(info.EntriesAdded, info.Length, info.Length > 0 ? (string?)info.FirstEntry.Id : null, (string?)info.LastGeneratedId, (string?)info.MaxDeletedEntryId);
    }

    /// <summary>
    /// The newest event that may be gone from the stream. Valkey records only what <c>XDEL</c> deleted
    /// (<c>max-deleted-entry-id</c>); trimming by length (<c>MAXLEN</c>, as <see cref="AppendAsync"/> does) leaves no
    /// trace but the count. So once anything was ever removed, everything before the oldest entry left counts as gone,
    /// or everything up to the last id when none is left. Erring this way only costs a page a reset.
    /// </summary>
    public static LiveEventId? TrimmedUpTo(long entriesAdded, long length, string? firstEntryId, string? lastGeneratedId, string? maxDeletedEntryId)
    {
        LiveEventId? gone = LiveEventId.TryParse(maxDeletedEntryId, out var deleted) && deleted > LiveEventId.Zero ? deleted : null;
        if (entriesAdded <= length)
        {
            return gone;
        }

        LiveEventId? beforeFirst = length > 0
            ? LiveEventId.TryParse(firstEntryId, out var first) ? Previous(first) : null
            : LiveEventId.TryParse(lastGeneratedId, out var last) ? last : null;
        return beforeFirst is { } trimmed && (gone is null || trimmed > gone) ? trimmed : gone;
    }

    private static LiveEventId Previous(LiveEventId id) =>
        id.Sequence > 0 ? new LiveEventId(id.Milliseconds, id.Sequence - 1) : new LiveEventId(Math.Max(0, id.Milliseconds - 1), long.MaxValue);

    public async Task<LiveEventId?> LastIdAsync(CancellationToken cancellationToken) =>
        Id(await (await redis.DatabaseAsync()).StreamRangeAsync(StreamKey, "-", "+", 1, Order.Descending));

    public Task MarkCountsChangedAsync(IReadOnlyCollection<Guid> beepIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beepIds);
        return beepIds.Count == 0
            ? Task.CompletedTask
            : EvaluateAsync(
                MarkScript, [ChangedCountsKey], [Milliseconds(KeyLifetime), MaxChangedCounts, .. beepIds.Select(id => (RedisValue)id.ToString("D"))]);
    }

    public async Task<IReadOnlyList<Guid>> TakeChangedCountsAsync(int max, CancellationToken cancellationToken)
    {
        var members = await (await redis.DatabaseAsync()).SetPopAsync(ChangedCountsKey, max);
        return [.. members.Select(m => Guid.TryParse((string?)m, CultureInfo.InvariantCulture, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
    }

    public async Task<IReadOnlyList<string>> AddConnectionAsync(Guid accountId, string connectionId, DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        var evicted = (RedisValue[])(await (await redis.DatabaseAsync()).ScriptEvaluateAsync(
            AddConnectionScript,
            [ConnectionsKey(accountId)],
            [connectionId, now.ToUnixTimeMilliseconds(), max, Milliseconds(KeyLifetime)]))!;
        return [.. evicted.Select(e => (string)e!)];
    }

    public async Task RemoveConnectionAsync(Guid accountId, string connectionId, CancellationToken cancellationToken) =>
        await (await redis.DatabaseAsync()).SortedSetRemoveAsync(ConnectionsKey(accountId), connectionId);

    private async Task EvaluateAsync(string script, RedisKey[] keys, RedisValue[] values) =>
        await (await redis.DatabaseAsync()).ScriptEvaluateAsync(script, keys, values);

    private static LiveEvent ToEvent(StreamEntry entry) =>
        new(entry.Id!, (string?)entry[TypeField] ?? "", (string?)entry[DataField] ?? "");

    private static LiveEventId? Id(StreamEntry[] entries) =>
        entries.Length == 0 ? null : LiveEventId.Parse(entries[0].Id!);

    private static RedisKey ConnectionsKey(Guid accountId) => $"{RedisSetup.KeyPrefix}sse:{accountId:D}";

    private static RedisValue Milliseconds(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
