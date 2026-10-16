using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Admin;

/// <summary>
/// The admin area's business rules (PLAN 3.6). Every method re-checks that the caller is an active
/// admin, and every write is logged in the same transaction as the change it records.
/// </summary>
public sealed partial class AdminService(
    IAdminStore store,
    IAccountStore accounts,
    ISessionStore sessions,
    IAgentStore agents,
    AgentService agentService,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    AvatarUrls avatars,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<AdminService> logger)
{
    public const int PageSize = 50;

    public const int MaxQueryLength = 100;

    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    /// <param name="query">An email prefix if it contains <c>@</c>, otherwise a username prefix.</param>
    public async Task<Result<CursorPage<AdminUserRow>>> ListUsersAsync(Guid adminId, string? query, Guid? before, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        query = query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return await store.ListUsersAsync(null, null, before, PageSize, cancellationToken);
        }

        if (query.Length > MaxQueryLength)
        {
            query = query[..MaxQueryLength];
        }

        return query.Contains('@', StringComparison.Ordinal) && !query.StartsWith('@')
            ? await store.ListUsersAsync(null, AccountRules.NormalizeEmail(query), before, PageSize, cancellationToken)
            : await store.ListUsersAsync(AccountRules.NormalizeHandle(query.TrimStart('@')), null, before, PageSize, cancellationToken);
    }

    public async Task<Result<AdminUserDetails>> GetUserAsync(Guid adminId, Guid userId, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var user = await store.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserNotFound();
        }

        var userAgents = await agentService.ListAsync(userId, cancellationToken);
        return new AdminUserDetails(user, avatars.For(user.Id, user.AvatarKey), userAgents);
    }

    /// <summary>Bans the user, ends all their sessions and disables all their agents (PLAN 3.6).</summary>
    public async Task<Result> BanUserAsync(Guid adminId, Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> agentIds = [];
        var result = await ChangeUserAsync(adminId, userId, async (user, now, ct) =>
        {
            if (user.BannedAt is not null)
            {
                return Result.Success;
            }

            if (await LastAdminAsync(user, ct) is { } lastAdmin)
            {
                return lastAdmin;
            }

            user.BannedAt = now;
            await sessions.RevokeAllAsync(user.Id, now, ct);
            agentIds = await agents.DisableAllAsync(user.Id, now, ct);
            outbox.SyncAccountToSearch(user.Id, beeps: true);
            Audit(adminId, AuditActions.BanUser, AuditTargets.User, user.Id, $"agents disabled: {agentIds.Count}");
            return Result.Success;
        }, cancellationToken);
        await ForgetAsync(userId, result, endSessions: true, cancellationToken);
        await agents.ForgetKeysAsync(agentIds, cancellationToken);
        return result;
    }

    /// <summary>Lifts a ban. The agents stay disabled until their owner turns them back on.</summary>
    public async Task<Result> UnbanUserAsync(Guid adminId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await ChangeUserAsync(adminId, userId, (user, _, _) =>
        {
            if (user.BannedAt is null)
            {
                return Task.FromResult(Result.Success);
            }

            user.BannedAt = null;
            outbox.SyncAccountToSearch(user.Id, beeps: true);
            Audit(adminId, AuditActions.UnbanUser, AuditTargets.User, user.Id, null);
            return Task.FromResult(Result.Success);
        }, cancellationToken);
        await ForgetAsync(userId, result, endSessions: false, cancellationToken);
        return result;
    }

    /// <summary>Soft-deletes the user and all their agents. Handle and email stay taken, so nobody can reappear as them.</summary>
    public async Task<Result> DeleteUserAsync(Guid adminId, Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> agentIds = [];
        var result = await ChangeUserAsync(adminId, userId, async (user, now, ct) =>
        {
            if (await LastAdminAsync(user, ct) is { } lastAdmin)
            {
                return lastAdmin;
            }

            user.Actor.DeletedAt = now;
            user.IsAdmin = false;
            await sessions.RevokeAllAsync(user.Id, now, ct);
            agentIds = await agents.DeleteAllAsync(user.Id, now, ct);
            outbox.SyncAccountToSearch(user.Id, beeps: true);
            Audit(adminId, AuditActions.DeleteUser, AuditTargets.User, user.Id, $"agents deleted: {agentIds.Count}");
            return Result.Success;
        }, cancellationToken);
        await ForgetAsync(userId, result, endSessions: true, cancellationToken);
        await agents.ForgetKeysAsync(agentIds, cancellationToken);
        return result;
    }

    /// <summary>Grants or revokes admin. The last active admin cannot be revoked; an admin may revoke themselves otherwise.</summary>
    public async Task<Result> SetAdminAsync(Guid adminId, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var result = await unitOfWork.InTransactionAsync(
            AdvisoryLocks.Admins,
            async ct =>
            {
                var user = await store.FindUserAsync(userId, ct);
                if (user is null)
                {
                    return UserNotFound();
                }

                if (user.IsAdmin == isAdmin)
                {
                    return Result.Success;
                }

                if (isAdmin && user.BannedAt is not null)
                {
                    return Error.Conflict(AdminErrorCodes.UserBanned, localizer["Error.UserBanned"]);
                }

                if (!isAdmin && await LastAdminAsync(user, ct) is { } lastAdmin)
                {
                    return lastAdmin;
                }

                user.IsAdmin = isAdmin;
                Audit(adminId, isAdmin ? AuditActions.GrantAdmin : AuditActions.RevokeAdmin, AuditTargets.User, user.Id, null);
                await unitOfWork.SaveChangesAsync(ct);
                LogAdminChanged(logger, user.Id, isAdmin, adminId);
                return Result.Success;
            },
            cancellationToken);
        await ForgetAsync(userId, result, endSessions: false, cancellationToken);
        return result;
    }

    public async Task<Result<RegistrationMode>> GetRegistrationModeAsync(Guid adminId, CancellationToken cancellationToken) =>
        await RequireAdminAsync(adminId, cancellationToken) is { } denied
            ? denied
            : await accounts.GetRegistrationModeAsync(cancellationToken);

    public async Task<Result> SetRegistrationModeAsync(Guid adminId, RegistrationMode mode, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            return Error.Validation(ErrorCodes.BadRequest, localizer["Error.InvalidInput"]);
        }

        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var current = await accounts.GetRegistrationModeAsync(cancellationToken);
        if (current == mode)
        {
            return Result.Success;
        }

        await store.SetRegistrationModeAsync(mode, time.GetUtcNow(), cancellationToken);
        Audit(adminId, AuditActions.SetRegistrationMode, AuditTargets.Site, null, $"{current} -> {mode}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    public async Task<Result<IReadOnlyList<InviteRow>>> ListInvitesAsync(Guid adminId, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        return Result.Of(await store.ListInvitesAsync(PageSize, cancellationToken));
    }

    public async Task<Result<CreatedInvite>> CreateInviteAsync(Guid adminId, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var now = time.GetUtcNow();
        var code = InviteCodes.Generate();
        var invite = new Invite
        {
            Id = Guid.CreateVersion7(now),
            CodeHash = InviteCodes.Hash(code),
            CodePrefix = InviteCodes.Prefix(code),
            CreatedBy = adminId,
            CreatedAt = now,
            ExpiresAt = now + InviteLifetime,
        };
        store.AddInvite(invite);
        Audit(adminId, AuditActions.CreateInvite, AuditTargets.Invite, invite.Id, invite.CodePrefix);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedInvite(new InviteRow(invite.Id, invite.CodePrefix, invite.CreatedAt, invite.ExpiresAt, null, null), code);
    }

    /// <summary>Withdraws an unused invite. Used ones stay, as the record of who invited whom.</summary>
    public async Task<Result> RevokeInviteAsync(Guid adminId, Guid inviteId, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        return await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                if (!await store.DeleteUnusedInviteAsync(inviteId, ct))
                {
                    return Error.NotFound(ErrorCodes.NotFound, localizer["Error.InviteNotFound"]);
                }

                Audit(adminId, AuditActions.RevokeInvite, AuditTargets.Invite, inviteId, null);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
    }

    public async Task<Result<CursorPage<AuditLogRow>>> ListAuditLogAsync(Guid adminId, Guid? before, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        return await store.ListAuditLogAsync(before, PageSize, cancellationToken);
    }

    /// <summary>The admin check, done here rather than trusted from the page: revoking admin takes effect at once.</summary>
    private async Task<Error?> RequireAdminAsync(Guid adminId, CancellationToken cancellationToken) =>
        await store.IsActiveAdminAsync(adminId, cancellationToken)
            ? null
            : Error.Forbidden(AdminErrorCodes.NotAdmin, localizer["Error.NotAdmin"]);

    /// <summary>Runs a change to another user under the admin lock, so the last-admin rule cannot be raced.</summary>
    private async Task<Result> ChangeUserAsync(
        Guid adminId,
        Guid userId,
        Func<User, DateTimeOffset, CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(adminId, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (userId == adminId)
        {
            return Error.Conflict(AdminErrorCodes.CannotTargetSelf, localizer["Error.CannotTargetSelf"]);
        }

        return await unitOfWork.InTransactionAsync(
            AdvisoryLocks.Admins,
            async ct =>
            {
                var user = await store.FindUserAsync(userId, ct);
                if (user is null)
                {
                    return UserNotFound();
                }

                var result = await change(user, time.GetUtcNow(), ct);
                if (result.IsSuccess)
                {
                    await unitOfWork.SaveChangesAsync(ct);
                }

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// After a committed change: drops the cached account state, so the change applies to the user's very
    /// next request, and with <paramref name="endSessions"/> the cached copies of their revoked sessions.
    /// </summary>
    private async Task ForgetAsync(Guid userId, Result result, bool endSessions, CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        if (endSessions)
        {
            await sessions.ForgetAllAsync(userId, cancellationToken);
        }

        await accounts.ForgetAccountAsync(userId, cancellationToken);
    }

    /// <summary>An error if taking <paramref name="user"/> out of the admin set would leave it empty.</summary>
    private async Task<Error?> LastAdminAsync(User user, CancellationToken cancellationToken) =>
        user.IsAdmin && user.BannedAt is null && await store.CountActiveAdminsAsync(cancellationToken) <= 1
            ? Error.Conflict(AdminErrorCodes.LastAdmin, localizer["Error.LastAdmin"])
            : null;

    private void Audit(Guid adminId, string action, string targetType, Guid? targetId, string? detail)
    {
        var now = time.GetUtcNow();
        store.AddAuditLog(new AdminAuditLog
        {
            Id = Guid.CreateVersion7(now),
            AdminId = adminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail,
            CreatedAt = now,
        });
    }

    private Error UserNotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.AccountNotFound"]);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin rights of {UserId} set to {IsAdmin} by {AdminId}")]
    private static partial void LogAdminChanged(ILogger logger, Guid userId, bool isAdmin, Guid adminId);
}
