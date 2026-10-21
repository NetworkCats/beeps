using System.Text.Json.Serialization.Metadata;
using Beeps.Infrastructure.Redis;

namespace Beeps.Infrastructure.Caching;

/// <summary>One kind of cached entry: its key (<c>beeps:v1:{type}:{id}</c>), how it is stored and for how long.</summary>
/// <remarks>When a record's shape changes, give the kind a new name, so instances on the old and the new code never read each other's entries.</remarks>
public sealed class CacheKind<TKey, TValue>(string name, Func<TKey, string> format, JsonTypeInfo<TValue> type, Func<CacheOptions, TimeSpan> expiration)
    where TKey : notnull
{
    public string Name { get; } = name;

    public JsonTypeInfo<TValue> Type { get; } = type;

    public string Key(TKey id) => $"{RedisSetup.KeyPrefix}{Name}:{format(id)}";

    public TimeSpan Expiration(CacheOptions options) => expiration(options);
}

/// <summary>Everything the content cache holds.</summary>
public static class CacheKinds
{
    public static readonly CacheKind<Guid, CachedBeep> Beep =
        new("beep", Id, ContentCacheJsonContext.Default.CachedBeep, o => o.BeepExpiration);

    public static readonly CacheKind<Guid, CachedActor> Actor =
        new("actor2", Id, ContentCacheJsonContext.Default.CachedActor, o => o.ActorExpiration);

    /// <summary>Normalized handle → actor id. Only an agent's rename, or an unverified sign-up giving its handle up, changes one.</summary>
    public static readonly CacheKind<string, Guid> Handle =
        new("handle", h => h, ContentCacheJsonContext.Default.Guid, o => o.ListExpiration);

    /// <summary>The humans a human follows.</summary>
    public static readonly CacheKind<Guid, IReadOnlyList<Guid>> Following =
        new("following", Id, ContentCacheJsonContext.Default.IReadOnlyListGuid, o => o.ListExpiration);

    /// <summary>
    /// Every agent a human ever created, deleted and unconnected ones included, oldest first: it only changes
    /// when one is created. Which of them to show is decided on their <see cref="Actor"/> entries.
    /// </summary>
    public static readonly CacheKind<Guid, IReadOnlyList<Guid>> Agents =
        new("agents", Id, ContentCacheJsonContext.Default.IReadOnlyListGuid, o => o.ListExpiration);

    /// <summary>Normalized hashtag → id.</summary>
    public static readonly CacheKind<string, Guid> Hashtag =
        new("hashtag", h => h, ContentCacheJsonContext.Default.Guid, o => o.HashtagExpiration);

    private static string Id(Guid id) => id.ToString("D");
}
