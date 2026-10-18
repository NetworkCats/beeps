using System.Linq.Expressions;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

/// <summary>
/// Admin and review reads go past <see cref="BeepVisibility"/> on purpose: they are about beeps that are hidden,
/// failed review or deleted, which nobody else may see.
/// </summary>
public sealed class EfModerationStore(BeepsDbContext db, PendingCacheChanges cacheChanges) : IModerationStore
{
    private const int MaxResults = 20;
    private const int MaxReports = 50;

    private static readonly Expression<Func<ModerationResult, ModerationResultRow>> ToResultRow = r => new ModerationResultRow(
        r.Id, r.Model, r.Provider, r.Decision, r.Categories, r.Severity, r.Reason, r.LatencyMs, r.Tokens, r.CreatedAt);

    public Task<ModerationSubject?> GetSubjectAsync(Guid beepId, CancellationToken cancellationToken) =>
        db.Set<Beep>()
            .AsNoTracking()
            .Where(b => b.Id == beepId)
            .Select(b => new ModerationSubject(
                b.Id,
                b.Text,
                b.ModerationStatus,
                b.DeletedAt != null,
                b.AuthorId,
                b.Author.OwnerId,
                b.ReplyToId,
                db.Set<Beep>().Where(p => p.Id == b.ReplyToId && p.DeletedAt == null).Select(p => p.Text).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public void AddResult(ModerationResult result) => db.Set<ModerationResult>().Add(result);

    public async Task<bool> SetStatusAsync(Guid beepId, IReadOnlyCollection<ModerationStatus> from, ModerationStatus to, CancellationToken cancellationToken)
    {
        var changed = await db.Set<Beep>()
            .Where(b => b.Id == beepId && b.DeletedAt == null && from.Contains(b.ModerationStatus))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ModerationStatus, to), cancellationToken) == 1;
        if (changed && to == ModerationStatus.Hidden)
        {
            // Whether a beep is hidden is never cached, only the pages showing it.
            cacheChanges.Add(CacheChange.BeepHidden(beepId));
        }
        else if (changed && from.Contains(ModerationStatus.Hidden))
        {
            // Shown again (when it was hidden): back on the pages it belongs on, which left it out meanwhile.
            cacheChanges.Add(CacheChange.BeepPosted(beepId));
        }

        return changed;
    }

    public Task DeleteJobAsync(Guid beepId, CancellationToken cancellationToken) =>
        db.Set<ModerationJob>().Where(j => j.BeepId == beepId).ExecuteDeleteAsync(cancellationToken);

    public async Task<bool> AddReportAsync(Report report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO reports (id, beep_id, reporter_id, reason, note, created_at)
            VALUES ({report.Id}, {report.BeepId}, {report.ReporterId}, {report.Reason}, {report.Note}, {report.CreatedAt})
            ON CONFLICT (beep_id, reporter_id) WHERE resolved_at IS NULL DO NOTHING
            """,
            cancellationToken) == 1;
    }

    public Task<int> ResolveReportsAsync(Guid beepId, Guid adminId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Report>()
            .Where(r => r.BeepId == beepId && r.ResolvedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ResolvedAt, now).SetProperty(r => r.ResolvedBy, adminId), cancellationToken);

    public async Task<CursorPage<ReviewQueueRow>> ListQueueAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        var queue = Queue();
        if (after is { } cursor)
        {
            queue = queue.Where(b => b.Id > cursor);
        }

        var rows = await queue
            .OrderBy(b => b.Id)
            .Take(limit + 1)
            .Select(b => new ReviewQueueRow(
                b.Id,
                b.Text,
                new ActorRow(b.Author.Id, b.Author.Kind, b.Author.Handle, b.Author.DisplayName, b.Author.OwnerId),
                b.ModerationStatus,
                b.CreatedAt,
                db.Set<Report>().Count(r => r.BeepId == b.Id && r.ResolvedAt == null),
                db.Set<ModerationResult>().Where(r => r.BeepId == b.Id).OrderByDescending(r => r.Id).Select(ToResultRow).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.BeepId);
    }

    /// <remarks>
    /// As <see cref="Queue"/> counts them, without looking the flagged beeps up again: <c>ix_beeps_review</c> lists only
    /// those not deleted, so only reported ones need the check. The admin pages count it every few seconds.
    /// </remarks>
    public Task<int> CountQueueAsync(CancellationToken cancellationToken)
    {
        var flagged = db.Set<Beep>()
            .Where(b => (b.ModerationStatus == ModerationStatus.Hidden || b.ModerationStatus == ModerationStatus.Failed) && b.DeletedAt == null)
            .Select(b => b.Id);
        var reported = db.Set<Report>()
            .Where(r => r.ResolvedAt == null)
            .Join(db.Set<Beep>().Where(b => b.DeletedAt == null), r => r.BeepId, b => b.Id, (r, b) => b.Id);
        return flagged.Union(reported).CountAsync(cancellationToken);
    }

    public async Task<ReviewBeepRow?> GetReviewBeepAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var beep = await db.Set<Beep>()
            .AsNoTracking()
            .Where(b => b.Id == beepId)
            .Select(b => new
            {
                b.Id,
                b.Text,
                Author = new ActorRow(b.Author.Id, b.Author.Kind, b.Author.Handle, b.Author.DisplayName, b.Author.OwnerId),
                OwnerHandle = db.Set<Actor>().Where(o => o.Id == b.Author.OwnerId).Select(o => o.Handle).FirstOrDefault(),
                b.RootId,
                b.ModerationStatus,
                b.CreatedAt,
                b.DeletedAt,
                ReplyTo = db.Set<Beep>()
                    .Where(p => p.Id == b.ReplyToId)
                    .Select(p => new ReviewContextRow(p.Id, p.Text, new ActorRow(p.Author.Id, p.Author.Kind, p.Author.Handle, p.Author.DisplayName, p.Author.OwnerId), p.DeletedAt))
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (beep is null)
        {
            return null;
        }

        var media = await db.Set<BeepMedia>()
            .AsNoTracking()
            .Where(bm => bm.BeepId == beepId)
            .Join(db.Set<MediaItem>(), bm => bm.MediaId, m => m.Id, (bm, m) => new { bm.Position, Row = new MediaRow(m.Id, m.StorageKey, m.Width, m.Height, m.Status) })
            .OrderBy(x => x.Position)
            .Select(x => x.Row)
            .ToListAsync(cancellationToken);
        var results = await db.Set<ModerationResult>()
            .AsNoTracking()
            .Where(r => r.BeepId == beepId)
            .OrderByDescending(r => r.Id)
            .Take(MaxResults)
            .Select(ToResultRow)
            .ToListAsync(cancellationToken);
        var reports = await db.Set<Report>()
            .AsNoTracking()
            .Where(r => r.BeepId == beepId)
            .OrderByDescending(r => r.Id)
            .Take(MaxReports)
            .Select(r => new ReportRow(
                r.Id,
                r.ReporterId,
                db.Set<Actor>().Where(a => a.Id == r.ReporterId).Select(a => a.Handle).FirstOrDefault(),
                r.Reason,
                r.Note,
                r.CreatedAt,
                r.ResolvedAt))
            .ToListAsync(cancellationToken);
        return new ReviewBeepRow(
            beep.Id, beep.Text, beep.Author, beep.OwnerHandle, beep.RootId, beep.ModerationStatus, beep.CreatedAt, beep.DeletedAt, beep.ReplyTo, media, results, reports);
    }

    public async Task<IReadOnlyList<string>> RemoveMediaAsync(Guid beepId, CancellationToken cancellationToken)
    {
        var removed = EnumText.ToValue(MediaStatus.Removed);
        var keys = await db.Database
            .SqlQuery<string>(
                $"""
                UPDATE media SET status = {removed}
                WHERE id IN (SELECT media_id FROM beep_media WHERE beep_id = {beepId}) AND status <> {removed}
                RETURNING storage_key AS "Value"
                """)
            .ToListAsync(cancellationToken);
        // An original still waiting to be processed would otherwise be stored once the job gets to it.
        await db.Set<MediaUpload>()
            .Where(u => db.Set<BeepMedia>().Any(bm => bm.BeepId == beepId && bm.MediaId == u.MediaId))
            .ExecuteDeleteAsync(cancellationToken);
        if (keys.Count > 0)
        {
            // The same image may also be someone's avatar or banner, which go back to the default pattern and the plain strip.
            var avatarUsers = await db.Set<Actor>()
                .Where(a => db.Set<BeepMedia>().Any(bm => bm.BeepId == beepId && (bm.MediaId == a.AvatarMediaId || bm.MediaId == a.BannerMediaId)))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
            // And on the other beeps it was attached to (an uploader may attach one image to several).
            var beeps = await db.Set<BeepMedia>()
                .Where(bm => db.Set<BeepMedia>().Any(o => o.BeepId == beepId && o.MediaId == bm.MediaId))
                .Select(bm => bm.BeepId)
                .Distinct()
                .ToListAsync(cancellationToken);
            cacheChanges.Add([CacheChange.Beep(beepId), .. beeps.Select(CacheChange.Beep), .. avatarUsers.Select(CacheChange.Actor)]);
        }

        return keys;
    }

    /// <summary>Beeps waiting for an admin: hidden by the AI, failed review, or reported, and not deleted.</summary>
    private IQueryable<Beep> Queue()
    {
        var flagged = db.Set<Beep>()
            .Where(b => (b.ModerationStatus == ModerationStatus.Hidden || b.ModerationStatus == ModerationStatus.Failed) && b.DeletedAt == null)
            .Select(b => b.Id)
            .Union(db.Set<Report>().Where(r => r.ResolvedAt == null).Select(r => r.BeepId));
        return db.Set<Beep>().AsNoTracking().Where(b => b.DeletedAt == null && flagged.Contains(b.Id));
    }
}
