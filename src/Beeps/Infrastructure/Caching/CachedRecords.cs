using System.Text.Json.Serialization;
using Beeps.Features.Accounts;
using Beeps.Features.Media;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// A beep as cached: only what is the same for every reader. Who may see it, whether the reader liked it and
/// whether it is hidden are asked of PostgreSQL on every read; the author and the mentioned actors are cached
/// as <see cref="CachedActor"/>, so a rename does not touch their beeps.
/// </summary>
/// <param name="LikeCount">The written-back column, shown only when the beep has no live counts.</param>
/// <param name="Mentions">Actors the text mentioned when it was posted (whether they are still visible is decided on read).</param>
/// <param name="Media">Ready images, in order.</param>
public sealed record CachedBeep(
    Guid Id,
    string Text,
    Guid AuthorId,
    Guid? ReplyToId,
    Guid RootId,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int ReplyCount,
    IReadOnlyList<Guid> Mentions,
    IReadOnlyList<MediaRow> Media);

/// <summary>
/// An actor as cached. Visibility is not stored but derived on read (<see cref="ContentReads"/>): an agent is
/// hidden when its owner is, and storing that here would mean dropping every agent's entry on a ban.
/// </summary>
/// <param name="Banned">For humans: banned. Agents are never banned themselves.</param>
/// <param name="BannerKey">Storage key of the profile banner, once it is ready.</param>
public sealed record CachedActor(
    Guid Id,
    ActorKind Kind,
    string? Handle,
    string? DisplayName,
    Guid? OwnerId,
    string? AvatarKey,
    string? Bio,
    string? ModelName,
    DateTimeOffset CreatedAt,
    bool Deleted,
    bool Banned,
    string? BannerKey = null);

/// <summary>Invalidation broadcast to every instance: drop these entries and these output-cached pages.</summary>
public sealed record CacheInvalidationMessage(IReadOnlyList<string> Keys, IReadOnlyList<string> PageTags);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CachedBeep))]
[JsonSerializable(typeof(CachedActor))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(IReadOnlyList<Guid>))]
[JsonSerializable(typeof(CacheInvalidationMessage))]
[JsonSerializable(typeof(CacheInvalidationRequest))]
internal sealed partial class ContentCacheJsonContext : JsonSerializerContext;
