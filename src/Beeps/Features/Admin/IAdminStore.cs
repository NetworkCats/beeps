using Beeps.Features.Accounts;
using Beeps.Features.Shared;

namespace Beeps.Features.Admin;

/// <summary>Data access for the admin area. Same conventions as the other stores: entities are tracked, writes are staged.</summary>
public interface IAdminStore
{
    /// <summary>An admin who is neither banned nor deleted.</summary>
    Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    /// <param name="handlePrefix">Normalized handle prefix, or null.</param>
    /// <param name="emailPrefix">Normalized email prefix, or null.</param>
    Task<CursorPage<AdminUserRow>> ListUsersAsync(string? handlePrefix, string? emailPrefix, Guid? before, int limit, CancellationToken cancellationToken);

    /// <summary>Includes deleted users, so their record can still be inspected.</summary>
    Task<AdminUserRow?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked, with its actor; null for deleted users.</summary>
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    void AddAuditLog(AdminAuditLog entry);

    Task<CursorPage<AuditLogRow>> ListAuditLogAsync(Guid? before, int limit, CancellationToken cancellationToken);

    /// <summary>Stages the new mode (insert or update).</summary>
    Task SetRegistrationModeAsync(RegistrationMode mode, DateTimeOffset now, CancellationToken cancellationToken);

    void AddInvite(Invite invite);

    Task<IReadOnlyList<InviteRow>> ListInvitesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Deletes the invite if nobody has used it yet. Runs immediately.</summary>
    Task<bool> DeleteUnusedInviteAsync(Guid inviteId, CancellationToken cancellationToken);
}

public sealed record AdminUserRow(
    Guid Id,
    string Handle,
    string DisplayName,
    string Email,
    bool IsAdmin,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? BannedAt,
    DateTimeOffset? DeletedAt);

public sealed record AuditLogRow(
    Guid Id,
    Guid AdminId,
    string AdminHandle,
    string Action,
    string TargetType,
    Guid? TargetId,
    string? TargetHandle,
    string? Detail,
    DateTimeOffset CreatedAt);

public sealed record InviteRow(
    Guid Id,
    string CodePrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    string? UsedByHandle);
