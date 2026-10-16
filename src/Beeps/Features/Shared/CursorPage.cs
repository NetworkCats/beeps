namespace Beeps.Features.Shared;

/// <summary>One page of a list ordered newest first. <see cref="NextCursor"/> is the id to pass as <c>before</c> for the next page, or null at the end.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);

public static class CursorPage
{
    /// <summary>Builds a page from a query that fetched <c>limit + 1</c> rows: the extra row only tells that more exist.</summary>
    public static CursorPage<T> FromOverfetch<T>(IReadOnlyList<T> rows, int limit, Func<T, Guid> id)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(id);
        return rows.Count > limit
            ? new CursorPage<T>([.. rows.Take(limit)], id(rows[limit - 1]))
            : new CursorPage<T>(rows, null);
    }
}
