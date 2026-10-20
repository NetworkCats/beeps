using System.Buffers.Binary;

namespace Beeps.Features.Shared;

/// <summary>
/// Which slice of a newest-first list to return: items older than <see cref="Before"/> (the previous
/// page's <c>next_cursor</c>) and newer than <see cref="SinceId"/> (the newest item already seen).
/// </summary>
public sealed record PageRequest(Guid? Before, Guid? SinceId, int Limit)
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    /// <summary>
    /// How long an item can be committed after the id it was given (ids are made before the insert, and two inserts can
    /// commit in either order). A list's newest page leaves out what is younger, so a poll from its newest id does not
    /// pass over it.
    /// </summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Only items with an id below this one. The newest id a page gives, which the next poll's since_id names, then
    /// never passes an item still to be committed, which would never be returned.
    /// </summary>
    public Guid? Until { get; init; }

    /// <summary>
    /// For the API's lists: a list's newest page (with no <see cref="Before"/>, polled with <see cref="SinceId"/> or
    /// not) holds only what was given its id <see cref="SettleTime"/> before <paramref name="now"/>.
    /// </summary>
    public PageRequest Settled(DateTimeOffset now) => Before is not null ? this : this with { Until = FirstIdAt(now - SettleTime) };

    /// <summary>The lowest UUIDv7 of that millisecond: every id made from then on sorts after it.</summary>
    private static Guid FirstIdAt(DateTimeOffset time)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes, time.ToUnixTimeMilliseconds() << 16);
        bytes[6] = 0x70;
        bytes[8] = 0x80;
        return new Guid(bytes, bigEndian: true);
    }

    public static PageRequest Create(Guid? before = null, Guid? sinceId = null, int? limit = null) =>
        new(before, sinceId, ClampLimit(limit));

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
}
