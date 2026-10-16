namespace Beeps.Features.Shared;

/// <summary>
/// Which slice of a newest-first list to return: items older than <see cref="Before"/> (the previous
/// page's <c>next_cursor</c>) and newer than <see cref="SinceId"/> (the newest item already seen).
/// </summary>
public sealed record PageRequest(Guid? Before, Guid? SinceId, int Limit)
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;

    public static PageRequest Create(Guid? before = null, Guid? sinceId = null, int? limit = null) =>
        new(before, sinceId, ClampLimit(limit));

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
}
