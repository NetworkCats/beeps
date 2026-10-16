namespace Beeps.Features.Shared;

/// <summary>
/// One page of a list ordered by id. <see cref="NextCursor"/> is the last id on the page, to pass back for
/// the next page (<c>before</c> for newest-first lists, <c>after</c> for oldest-first ones), or null at the end.
/// </summary>
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
