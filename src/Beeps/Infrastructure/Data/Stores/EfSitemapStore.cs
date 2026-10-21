using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Crawlers;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>
/// Chunks by id (time-ordered). A beep's chunk is cut from the public timeline's index (<c>ix_beeps_public</c>), which
/// holds exactly the top-level beeps not deleted; whether each is visible is asked of those rows alone.
/// </summary>
public sealed class EfSitemapStore(BeepsDbContext db) : ISitemapStore
{
    public async Task<IReadOnlyList<Guid>> ListProfileChunksAsync(int size, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM (
                SELECT id, row_number() OVER (ORDER BY id) AS n FROM actors WHERE handle IS NOT NULL AND deleted_at IS NULL
            ) AS profiles
            WHERE (n - 1) % {size} = 0
            """)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListProfilesAsync(Guid from, int size, CancellationToken cancellationToken)
    {
        var chunk = db.Set<Actor>()
            .Where(a => a.Handle != null && a.DeletedAt == null && a.Id >= from)
            .OrderBy(a => a.Id)
            .Take(size)
            .Select(a => a.Id);
        return await BeepQueries.VisibleActors(db)
            .Where(a => chunk.Contains(a.Id))
            .OrderBy(a => a.Id)
            .Select(a => a.Handle!)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListBeepChunksAsync(int size, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM (
                SELECT id, row_number() OVER (ORDER BY id) AS n FROM beeps WHERE reply_to_id IS NULL AND deleted_at IS NULL
            ) AS beeps
            WHERE (n - 1) % {size} = 0
            """)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SitemapBeep>> ListBeepsAsync(Guid from, int size, CancellationToken cancellationToken)
    {
        var chunk = db.Set<Beep>()
            .Where(b => b.ReplyToId == null && b.DeletedAt == null && b.Id >= from)
            .OrderBy(b => b.Id)
            .Take(size)
            .Select(b => b.Id);
        return await BeepQueries.Visible(db, null)
            .Where(b => chunk.Contains(b.Id))
            .OrderBy(b => b.Id)
            .Select(b => new SitemapBeep(b.Id, b.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
