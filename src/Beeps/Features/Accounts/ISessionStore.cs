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

    /// <summary>Whether the session is still signed in: not revoked, not expired. Always asks the database.</summary>
    Task<bool> IsActiveAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends one session (sign-out). Runs immediately, cache included.</summary>
    Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends one of the user's sessions from another (removing a device). Runs immediately, cache included; false when the user has no such live session.</summary>
    Task<bool> RevokeByIdAsync(Guid userId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The user's live sessions, <paramref name="currentSessionId"/> first, then the most recently used, at most
    /// <paramref name="limit"/>. Always asks the database.
    /// </summary>
    Task<IReadOnlyList<ActiveSession>> ListActiveAsync(Guid userId, Guid? currentSessionId, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Ends every session of the user (password reset, ban, delete). Runs immediately, inside the caller's transaction if any.</summary>
    Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends every session of the user except <paramref name="keepSessionId"/> (password change). Runs immediately, inside the caller's transaction if any.</summary>
    Task RevokeOthersAsync(Guid userId, Guid keepSessionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Drops the cached copies of the user's sessions. Call after committing <see cref="RevokeAllAsync"/> or <see cref="RevokeOthersAsync"/>. Not cancellable: a client that goes away must not leave a revoked session cached.</summary>
    Task ForgetAllAsync(Guid userId);

    /// <summary>As <see cref="ForgetAllAsync"/>, for many users at once (one query, one removal).</summary>
    Task ForgetUsersAsync(IReadOnlyCollection<Guid> userIds);

    /// <summary>Deletes up to <paramref name="batchSize"/> sessions that expired or were revoked before <paramref name="cutoff"/>. Runs immediately; returns how many.</summary>
    Task<int> DeleteEndedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}

public sealed record SessionRow(Guid Id, Guid UserId, string TokenHash, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);

/// <summary>A live session as the devices page lists it.</summary>
public sealed record ActiveSession(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? UserAgent, string? IpAddress);

/// <summary>A live session and the account behind it, as one request sees it.</summary>
/// <param name="Renewed">This request pushed <paramref name="ExpiresAt"/> forward, so the cookie should be re-issued.</param>
public sealed record SignedInSession(Guid SessionId, AccountState Account, DateTimeOffset ExpiresAt, bool Renewed);

/// <summary>Where a sign-in came from; recorded on the session.</summary>
public sealed record SessionClient(string? UserAgent, string? IpAddress);

/// <summary>A signed-in device as the account's settings list it.</summary>
/// <param name="Place">Roughly where it signed in from; null when that is not known.</param>
/// <param name="IsCurrent">The device the list was asked from.</param>
public sealed record SignedInDevice(Guid Id, DeviceInfo Device, DevicePlace? Place, DateTimeOffset SignedInAt, DateTimeOffset LastSeenAt, bool IsCurrent);

/// <summary>A town or region, and its country, either of which may be unknown.</summary>
public sealed record DevicePlace(string? Locality, string? Country);
