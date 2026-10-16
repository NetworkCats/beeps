namespace Beeps.Features.Accounts;

/// <summary>
/// Account data access. Methods returning entities return them tracked: changes are staged and only
/// written by <see cref="Shared.IUnitOfWork"/>. Deleted accounts are never returned.
/// </summary>
public interface IAccountStore
{
    Task<bool> AnyUsersAsync(CancellationToken cancellationToken);

    /// <summary>Deleted actors still hold their handle.</summary>
    Task<bool> HandleTakenAsync(string normalizedHandle, CancellationToken cancellationToken);

    Task<bool> EmailTakenAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Stages a user together with its <see cref="User.Actor"/>.</summary>
    void AddUser(User user);

    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> FindUserByHandleAsync(string normalizedHandle, CancellationToken cancellationToken);

    /// <summary>
    /// What a signed-in request needs to know about its account; null if it is gone, deleted or banned.
    /// Served from a shared Valkey cache; <see cref="ForgetAccountAsync"/> drops it after a change.
    /// </summary>
    Task<AccountState?> GetAccountStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Drops the cached <see cref="AccountState"/>. Call after committing any change to it.</summary>
    Task ForgetAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary><see cref="RegistrationMode.Open"/> until an admin changes it.</summary>
    Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken);

    /// <summary>Whether an unused, unexpired invite with this hash exists (a read; it does not reserve it).</summary>
    Task<bool> IsInviteUsableAsync(string codeHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an unused, unexpired invite as used by <paramref name="userId"/>, atomically, so one code can
    /// never admit two people. Returns false if no such invite exists. Runs immediately (inside the
    /// caller's transaction, if any).
    /// </summary>
    Task<bool> TryUseInviteAsync(string codeHash, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<EmailCode?> FindEmailCodeAsync(EmailCodePurpose purpose, string normalizedEmail, CancellationToken cancellationToken);

    void AddEmailCode(EmailCode code);

    void RemoveEmailCode(EmailCode code);

    /// <summary>
    /// Counts one guess against a live code, atomically: false when there is no unexpired code with
    /// attempts left, so parallel guesses cannot exceed <paramref name="maxAttempts"/>. Runs immediately.
    /// </summary>
    Task<bool> TryCountCodeAttemptAsync(EmailCodePurpose purpose, string normalizedEmail, DateTimeOffset now, int maxAttempts, CancellationToken cancellationToken);
}

/// <summary>The signed-in account as every request sees it: rights, verification and language take effect at once.</summary>
public sealed record AccountState(
    Guid UserId,
    string Handle,
    string DisplayName,
    bool IsAdmin,
    bool EmailVerified,
    string Locale);
