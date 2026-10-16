using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfAdminStore(BeepsDbContext db) : IAdminStore
{
    public Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        ActiveAdmins().AnyAsync(u => u.Id == userId, cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        ActiveAdmins().CountAsync(cancellationToken);

    public async Task<CursorPage<AdminUserRow>> ListUsersAsync(string? handlePrefix, string? emailPrefix, Guid? before, int limit, CancellationToken cancellationToken)
    {
        var users = db.Set<User>().AsNoTracking();
        if (handlePrefix is not null)
        {
            users = users.Where(u => EF.Functions.Like(u.Actor.NormalizedHandle!, LikePrefix(handlePrefix), LikeEscape));
        }

        if (emailPrefix is not null)
        {
            users = users.Where(u => EF.Functions.Like(u.NormalizedEmail, LikePrefix(emailPrefix), LikeEscape));
        }

        if (before is { } cursor)
        {
            users = users.Where(u => u.Id < cursor);
        }

        var rows = await users
            .OrderByDescending(u => u.Id)
            .Take(limit + 1)
            .Select(ToRow())
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.Id);
    }

    public Task<AdminUserRow?> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(ToRow()).FirstOrDefaultAsync(cancellationToken);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().Include(u => u.Actor).FirstOrDefaultAsync(u => u.Id == userId && u.Actor.DeletedAt == null, cancellationToken);

    public void AddAuditLog(AdminAuditLog entry) => db.Set<AdminAuditLog>().Add(entry);

    public async Task<CursorPage<AuditLogRow>> ListAuditLogAsync(Guid? before, int limit, CancellationToken cancellationToken)
    {
        var entries = db.Set<AdminAuditLog>().AsNoTracking();
        if (before is { } cursor)
        {
            entries = entries.Where(l => l.Id < cursor);
        }

        var rows = await entries
            .OrderByDescending(l => l.Id)
            .Take(limit + 1)
            .Join(db.Set<Actor>(), l => l.AdminId, a => a.Id, (l, admin) => new { l, admin })
            .LeftJoin(db.Set<Actor>(), x => x.l.TargetId, t => t.Id, (x, target) => new { x.l, x.admin, target })
            .OrderByDescending(x => x.l.Id)
            .Select(x => new AuditLogRow(
                x.l.Id,
                x.l.AdminId,
                x.admin.Handle!,
                x.l.Action,
                x.l.TargetType,
                x.l.TargetId,
                x.target == null ? null : x.target.Handle,
                x.l.Detail,
                x.l.CreatedAt))
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.Id);
    }

    public async Task SetRegistrationModeAsync(RegistrationMode mode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var value = EnumText.ToValue(mode);
        var setting = await db.Set<SiteSetting>().FirstOrDefaultAsync(s => s.Key == SiteSetting.RegistrationModeKey, cancellationToken);
        if (setting is null)
        {
            db.Set<SiteSetting>().Add(new SiteSetting { Key = SiteSetting.RegistrationModeKey, Value = value, UpdatedAt = now });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }
    }

    public void AddInvite(Invite invite) => db.Set<Invite>().Add(invite);

    public async Task<IReadOnlyList<InviteRow>> ListInvitesAsync(int limit, CancellationToken cancellationToken) =>
        await db.Set<Invite>()
            .AsNoTracking()
            .OrderByDescending(i => i.Id)
            .Take(limit)
            .LeftJoin(db.Set<Actor>(), i => i.UsedBy, a => a.Id, (i, usedBy) => new { i, usedBy })
            .OrderByDescending(x => x.i.Id)
            .Select(x => new InviteRow(
                x.i.Id,
                x.i.CodePrefix,
                x.i.CreatedAt,
                x.i.ExpiresAt,
                x.i.UsedAt,
                x.usedBy == null ? null : x.usedBy.Handle))
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteUnusedInviteAsync(Guid inviteId, CancellationToken cancellationToken) =>
        await db.Set<Invite>().Where(i => i.Id == inviteId && i.UsedBy == null).ExecuteDeleteAsync(cancellationToken) == 1;

    private const string LikeEscape = "\\";

    private static string LikePrefix(string prefix) =>
        prefix.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    private static System.Linq.Expressions.Expression<Func<User, AdminUserRow>> ToRow() => u => new AdminUserRow(
        u.Id,
        u.Actor.Handle!,
        u.Actor.DisplayName!,
        u.Email,
        u.IsAdmin,
        u.EmailVerifiedAt != null,
        u.CreatedAt,
        u.BannedAt,
        u.Actor.DeletedAt);

    private IQueryable<User> ActiveAdmins() =>
        db.Set<User>().Where(u => u.IsAdmin && u.BannedAt == null && u.Actor.DeletedAt == null);
}
