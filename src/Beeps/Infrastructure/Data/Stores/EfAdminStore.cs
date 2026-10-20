using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Media;
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
            users = users.Where(u => EF.Functions.Like(u.Actor.NormalizedHandle!, SqlLike.Prefix(handlePrefix), SqlLike.Escape));
        }

        if (emailPrefix is not null)
        {
            users = users.Where(u => EF.Functions.Like(u.NormalizedEmail, SqlLike.Prefix(emailPrefix), SqlLike.Escape));
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

    public async Task<CursorPage<AdminAgentRow>> ListAgentsAsync(string? handlePrefix, Guid? before, int limit, CancellationToken cancellationToken)
    {
        var agents = db.Set<Actor>().AsNoTracking().Where(a => a.Kind == ActorKind.Agent);
        if (handlePrefix is not null)
        {
            agents = agents.Where(a => EF.Functions.Like(a.NormalizedHandle!, SqlLike.Prefix(handlePrefix), SqlLike.Escape));
        }

        if (before is { } cursor)
        {
            agents = agents.Where(a => a.Id < cursor);
        }

        var rows = await agents
            .OrderByDescending(a => a.Id)
            .Take(limit + 1)
            .Join(db.Set<Actor>(), a => a.OwnerId, o => (Guid?)o.Id, (a, o) => new { a, o })
            .LeftJoin(db.Set<AgentKey>().Where(k => k.RevokedAt == null), x => x.a.Id, k => k.AgentId, (x, k) => new { x.a, x.o, k })
            .OrderByDescending(x => x.a.Id)
            .Select(x => new AdminAgentRow(
                x.a.Id,
                x.a.Handle,
                x.a.ModelName,
                x.o.Id,
                x.o.Handle!,
                x.a.DisabledAt,
                x.a.DeletedAt,
                x.k == null ? null : x.k.KeyPrefix))
            .ToListAsync(cancellationToken);
        return CursorPage.FromOverfetch(rows, limit, r => r.Id);
    }

    public async Task<IReadOnlyList<AdminOwnedAgentRow>> ListOwnedAgentsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Set<Actor>()
            .AsNoTracking()
            .Where(a => a.Kind == ActorKind.Agent && a.OwnerId == ownerId)
            .LeftJoin(db.Set<AgentKey>().Where(k => k.RevokedAt == null), a => a.Id, k => k.AgentId, (a, k) => new { a, k })
            .OrderBy(x => x.a.Id)
            .Select(x => new AdminOwnedAgentRow(
                x.a.Id,
                x.a.Handle,
                x.a.DisplayName,
                x.a.ModelName,
                x.a.CreatedAt,
                x.a.DisabledAt,
                x.a.DeletedAt,
                x.k == null ? null : x.k.KeyPrefix,
                x.a.AvatarMedia == null || x.a.AvatarMedia.Status != MediaStatus.Ready ? null : x.a.AvatarMedia.StorageKey))
            .ToListAsync(cancellationToken);

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
                x.l.CreatedAt,
                x.target == null ? null : x.target.OwnerId))
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

    private static System.Linq.Expressions.Expression<Func<User, AdminUserRow>> ToRow() => u => new AdminUserRow(
        u.Id,
        u.Actor.Handle!,
        u.Actor.DisplayName!,
        u.Email,
        u.IsAdmin,
        u.EmailVerifiedAt != null,
        u.CreatedAt,
        u.BannedAt,
        u.Actor.DeletedAt,
        u.Actor.AvatarMedia == null || u.Actor.AvatarMedia.Status != MediaStatus.Ready ? null : u.Actor.AvatarMedia.StorageKey);

    // Verified too, as the admin pages ask (AuthPolicies.Admin): an unverified admin cannot reach them, so does not count
    // as the one left when the others step down.
    private IQueryable<User> ActiveAdmins() =>
        db.Set<User>().Where(u => u.IsAdmin && u.EmailVerifiedAt != null && u.BannedAt == null && u.Actor.DeletedAt == null);
}
