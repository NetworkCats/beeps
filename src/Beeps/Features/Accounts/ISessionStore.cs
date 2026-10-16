namespace Beeps.Features.Accounts;

/// <summary>
/// Server-side sessions. Lookups are served from a shared Valkey cache backed by PostgreSQL; every
/// method that changes a session also drops its cached copy, so a revoke takes effect on the next request.
/// </summary>
public interface ISessionStore
{
    /// <summary>Stages a new session; <see cref="Shared.IUnitOfWork"/> writes it.</summary>
    void Add(UserSession session);

    /// <summary>The session with this token hash unless it was revoked. Expiry is the caller's check (it needs the clock).</summary>
    Task<SessionRow?> FindAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Records activity and pushes the expiry out. Runs immediately.</summary>
    Task TouchAsync(SessionRow session, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Ends one session (sign-out). Runs immediately, cache included.</summary>
    Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends every session of the user (password reset, ban, delete). Runs immediately, inside the caller's transaction if any.</summary>
    Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Drops the cached copies of the user's sessions. Call after committing <see cref="RevokeAllAsync"/>.</summary>
    Task ForgetAllAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> sessions that expired or were revoked before <paramref name="cutoff"/>. Runs immediately; returns how many.</summary>
    Task<int> DeleteEndedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public sealed record SessionRow(Guid Id, Guid UserId, string TokenHash, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);

/// <summary>A live session and the account behind it, as one request sees it.</summary>
/// <param name="Renewed">This request pushed <paramref name="ExpiresAt"/> forward, so the cookie should be re-issued.</param>
public sealed record SignedInSession(Guid SessionId, AccountState Account, DateTimeOffset ExpiresAt, bool Renewed);

/// <summary>Where a sign-in came from; recorded on the session.</summary>
public sealed record SessionClient(string? UserAgent, string? IpAddress);
