using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beeps.Features.Shared;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(IdempotencyRecord))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext;

public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    // KEYS[1]: the key. ARGV: pending record, claim duration in ms. Returns the stored record, or nil once claimed.
    private const string ClaimScript = """
        local existing = redis.call('GET', KEYS[1])
        if existing then return existing end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        return false
        """;

    public async Task<IdempotencyRecord?> TryClaimAsync(string key, string fingerprint, TimeSpan claimDuration, CancellationToken cancellationToken)
    {
        var pending = JsonSerializer.Serialize(new IdempotencyRecord(fingerprint, null, null, null, null), IdempotencyJsonContext.Default.IdempotencyRecord);
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            ClaimScript,
            [Key(key)],
            [pending, ((long)claimDuration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)]);
        return result.IsNull ? null : JsonSerializer.Deserialize((string)result!, IdempotencyJsonContext.Default.IdempotencyRecord);
    }

    public Task CompleteAsync(string key, IdempotencyRecord record, TimeSpan retention, CancellationToken cancellationToken) =>
        redis.GetDatabase().StringSetAsync(Key(key), JsonSerializer.Serialize(record, IdempotencyJsonContext.Default.IdempotencyRecord), retention);

    public Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(Key(key));

    private static RedisKey Key(string key) => $"{RedisSetup.KeyPrefix}idempotency:{key}";
}
