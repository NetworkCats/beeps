using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Microsoft.EntityFrameworkCore;

namespace Beeps.Infrastructure.Data.Stores;

public sealed class EfAccountStore(BeepsDbContext db) : IAccountStore
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

    public Task<AccountSession?> GetSessionAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>()
            .Where(u => u.Id == userId && u.BannedAt == null && u.Actor.DeletedAt == null)
            .Select(u => new AccountSession(
                u.Id,
                u.Actor.Handle!,
                u.Actor.DisplayName!,
                u.IsAdmin,
                u.EmailVerifiedAt != null,
                u.Locale,
                u.SecurityStamp))
            .FirstOrDefaultAsync(cancellationToken);

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

    public void RemoveEmailCode(EmailCode code) => db.Set<EmailCode>().Remove(code);

    public async Task<bool> TryCountCodeAttemptAsync(EmailCodePurpose purpose, string normalizedEmail, DateTimeOffset now, int maxAttempts, CancellationToken cancellationToken) =>
        await db.Set<EmailCode>()
            .Where(c => c.Purpose == purpose && c.NormalizedEmail == normalizedEmail && c.ExpiresAt > now && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), cancellationToken) == 1;

    private IQueryable<User> Users() => db.Set<User>().Include(u => u.Actor).Where(u => u.Actor.DeletedAt == null);
}
