using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AgentKeyMatch))]
[JsonSerializable(typeof(SessionRow))]
[JsonSerializable(typeof(AccountState))]
internal sealed partial class CacheJsonContext : JsonSerializerContext;

/// <summary>HybridCache serializer backed by System.Text.Json source generation (PLAN 12: no reflection-based JSON).</summary>
internal sealed class SourceGeneratedCacheSerializer<T>(JsonTypeInfo<T> typeInfo) : IHybridCacheSerializer<T>
{
    public T Deserialize(ReadOnlySequence<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        return JsonSerializer.Deserialize(ref reader, typeInfo)!;
    }

    public void Serialize(T value, IBufferWriter<byte> target)
    {
        using var writer = new Utf8JsonWriter(target);
        JsonSerializer.Serialize(writer, value, typeInfo);
    }
}

/// <summary>
/// Cache entries that decide who a request is (agent keys, sessions, account state). They are kept in
/// Valkey only, with no per-instance copy: a change removes the entry, and a local copy on another
/// instance could not be removed, so it would keep a revoked key or session working.
/// </summary>
internal static class SharedCache
{
    public static readonly HybridCacheEntryOptions AgentKey = SharedOnly(TimeSpan.FromMinutes(1));

    public static readonly HybridCacheEntryOptions Session = SharedOnly(TimeSpan.FromMinutes(10));

    public static readonly HybridCacheEntryOptions Account = SharedOnly(TimeSpan.FromMinutes(10));

    public static string AgentKeyKey(string keyHash) => $"agentkey:{keyHash}";

    public static string SessionKey(string tokenHash) => $"session:{tokenHash}";

    public static string AccountKey(Guid userId) => $"account:{userId:D}";

    public static IHybridCacheBuilder AddBeepsCacheSerializers(this IHybridCacheBuilder builder) =>
        builder
            .AddSerializer(new SourceGeneratedCacheSerializer<AgentKeyMatch>(CacheJsonContext.Default.AgentKeyMatch))
            .AddSerializer(new SourceGeneratedCacheSerializer<SessionRow>(CacheJsonContext.Default.SessionRow))
            .AddSerializer(new SourceGeneratedCacheSerializer<AccountState>(CacheJsonContext.Default.AccountState));

    private static HybridCacheEntryOptions SharedOnly(TimeSpan expiration) =>
        new() { Expiration = expiration, Flags = HybridCacheEntryFlags.DisableLocalCache };
}
