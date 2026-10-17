using Beeps.Features.Accounts;
using Beeps.Features.Media;
using Beeps.Features.Site;
using Beeps.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfAccountStore(BeepsDbContext db, HybridCache cache, PendingCacheChanges cacheChanges) : IAccountStore
{
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(cancellationToken);

    public Task<bool> HandleTakenAsync(string normalizedHandle, CancellationToken cancellationToken) =>
        db.Set<Actor>().AnyAsync(a => a.NormalizedHandle == normalizedHandle, cancellationToken);

    public Task<bool> EmailTakenAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public void AddUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        db.Set<Actor>().Add(user.Actor);
        db.Set<User>().Add(user);
    }

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Users().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Users().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<User?> FindUserByHandleAsync(string normalizedHandle, CancellationToken cancellationToken) =>
        Users().FirstOrDefaultAsync(u => u.Actor.NormalizedHandle == normalizedHandle, cancellationToken);

    public async Task<AccountState?> GetAccountStateAsync(Guid userId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            SharedCache.AccountKey(userId),
            (db, userId),
            static async (state, ct) => await state.db.Set<User>()
                .Where(u => u.Id == state.userId && u.BannedAt == null && u.Actor.DeletedAt == null)
                .Select(u => new AccountState(
                    u.Id,
                    u.Actor.Handle!,
                    u.Actor.DisplayName!,
                    u.IsAdmin,
                    u.EmailVerifiedAt != null,
                    u.Locale,
                    u.Actor.AvatarMedia == null || u.Actor.AvatarMedia.Status != MediaStatus.Ready ? null : u.Actor.AvatarMedia.StorageKey,
                    u.Theme))
                .FirstOrDefaultAsync(ct),
            SharedCache.Account,
            cancellationToken: cancellationToken);

    public async Task ForgetAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await cache.RemoveAsync(SharedCache.AccountKey(userId), cancellationToken);

    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken)
    {
        var value = await db.Set<SiteSetting>()
            .Where(s => s.Key == SiteSetting.RegistrationModeKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return value is null ? RegistrationMode.Open : EnumText.Parse<RegistrationMode>(value);
    }

    public Task<bool> IsInviteUsableAsync(string codeHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Invite>().AnyAsync(i => i.CodeHash == codeHash && i.UsedBy == null && i.ExpiresAt > now, cancellationToken);

    public async Task<bool> TryUseInviteAsync(string codeHash, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Set<Invite>()
            .Where(i => i.CodeHash == codeHash && i.UsedBy == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedBy, userId).SetProperty(i => i.UsedAt, now), cancellationToken) == 1;

    public Task<EmailCode?> FindEmailCodeAsync(EmailCodePurpose purpose, string normalizedEmail, CancellationToken cancellationToken) =>
        db.Set<EmailCode>().FirstOrDefaultAsync(c => c.Purpose == purpose && c.NormalizedEmail == normalizedEmail, cancellationToken);

    public void AddEmailCode(EmailCode code) => db.Set<EmailCode>().Add(code);

    public Task<int> DeleteExpiredEmailCodesAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        db.Set<EmailCode>()
            .Where(c => c.ExpiresAt < cutoff)
            .OrderBy(c => c.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);

    public void RemoveEmailCode(EmailCode code) => db.Set<EmailCode>().Remove(code);

    /// <remarks>
    /// The email becomes <c>released:{id}</c> and the handle is cleared from the unique index, so both can be taken
    /// again; the row stays (like any deleted account) because others may have mentioned the handle. SKIP LOCKED leaves
    /// alone an account that is verifying right now.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> ReleaseUnverifiedAsync(DateTimeOffset registeredBefore, DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
    {
        var released = await db.Database
            .SqlQuery<ReleasedAccount>(
                $"""
                WITH due AS (
                    SELECT u.id FROM users u
                    JOIN actors a ON a.id = u.id AND a.deleted_at IS NULL
                    WHERE u.email_verified_at IS NULL AND u.created_at < {registeredBefore} AND NOT u.is_admin
                      AND NOT EXISTS (SELECT 1 FROM invites i WHERE i.used_by = u.id)
                    ORDER BY u.created_at
                    LIMIT {batchSize}
                    FOR UPDATE OF u SKIP LOCKED),
                emails AS (
                    UPDATE users u SET normalized_email = 'released:' || u.id::text FROM due WHERE u.id = due.id RETURNING u.id),
                sessions AS (
                    UPDATE user_sessions s SET revoked_at = {now} FROM due WHERE s.user_id = due.id AND s.revoked_at IS NULL)
                UPDATE actors a SET deleted_at = {now}, normalized_handle = NULL
                FROM due JOIN actors old ON old.id = due.id
                WHERE a.id = due.id
                RETURNING a.id AS "Id", old.normalized_handle AS "Handle"
                """)
            .ToListAsync(cancellationToken);
        cacheChanges.Add([
            .. released.Select(r => CacheChange.ActorVisibility(r.Id)),
            .. released.Where(r => r.Handle is not null).Select(r => CacheChange.Handle(r.Handle!))]);
        return [.. released.Select(r => r.Id)];
    }

    public async Task<bool> TryCountCodeAttemptAsync(EmailCodePurpose purpose, string normalizedEmail, DateTimeOffset now, int maxAttempts, CancellationToken cancellationToken) =>
        await db.Set<EmailCode>()
            .Where(c => c.Purpose == purpose && c.NormalizedEmail == normalizedEmail && c.ExpiresAt > now && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken) == 1;

    private IQueryable<User> Users() => db.Set<User>().Include(u => u.Actor).Where(u => u.Actor.DeletedAt == null);
}

internal sealed record ReleasedAccount(Guid Id, string? Handle);
