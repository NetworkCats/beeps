using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Agents;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AgentKeyMatch))]
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

internal static class AgentKeyCache
{
    /// <summary>
    /// Shared (Valkey) only, no per-instance copy: revoking a key removes this entry, and a local copy on
    /// another instance could not be removed, so it would keep the old key alive.
    /// </summary>
    public static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(1),
        Flags = HybridCacheEntryFlags.DisableLocalCache,
    };

    public static string Key(string keyHash) => $"agentkey:{keyHash}";

    public static IHybridCacheBuilder AddAgentKeyCacheSerializer(this IHybridCacheBuilder builder) =>
        builder.AddSerializer(new SourceGeneratedCacheSerializer<AgentKeyMatch>(CacheJsonContext.Default.AgentKeyMatch));
}
