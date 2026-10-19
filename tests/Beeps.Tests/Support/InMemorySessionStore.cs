using System.Collections.Concurrent;
using Beeps.Features.Accounts;

namespace Beeps.Tests.Support;

/// <summary>A working session store for the hosted-app tests, so sign-in, sign-out and revocation run end to end.</summary>
internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, UserSession> sessions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<UserSession> All => [.. sessions.Values];

    public void Add(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        sessions[session.TokenHash] = session;
    }

    public Task<SessionRow?> FindAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.TryGetValue(tokenHash, out var s) && s.RevokedAt is null
            ? new SessionRow(s.Id, s.UserId, s.TokenHash, s.LastSeenAt, s.ExpiresAt)
            : null);

    public Task TouchAsync(SessionRow session, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        Update(session.TokenHash, s => new UserSession
        {
            Id = s.Id, UserId = s.UserId, TokenHash = s.TokenHash, CreatedAt = s.CreatedAt,
            LastSeenAt = now, ExpiresAt = expiresAt, RevokedAt = s.RevokedAt, UserAgent = s.UserAgent, IpAddress = s.IpAddress,
        });
        return Task.CompletedTask;
    }

    public Task<bool> IsActiveAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.Values.Any(s => s.Id == sessionId && s.RevokedAt is null && s.ExpiresAt > now));

    public Task RevokeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Update(tokenHash, s => Revoked(s, now));
        return Task.CompletedTask;
    }

    public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var session in sessions.Values.Where(s => s.UserId == userId))
        {
            Update(session.TokenHash, s => Revoked(s, now));
        }

        return Task.CompletedTask;
    }

    public Task RevokeOthersAsync(Guid userId, Guid keepSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var session in sessions.Values.Where(s => s.UserId == userId && s.Id != keepSessionId))
        {
            Update(session.TokenHash, s => Revoked(s, now));
        }

        return Task.CompletedTask;
    }

    public Task ForgetAllAsync(Guid userId) => Task.CompletedTask;

    public Task ForgetUsersAsync(IReadOnlyCollection<Guid> userIds) => Task.CompletedTask;

    public Task<int> DeleteEndedAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var ended = sessions.Values.Where(s => s.ExpiresAt < cutoff || s.RevokedAt < cutoff).Take(batchSize).ToList();
        return Task.FromResult(ended.Count(s => sessions.TryRemove(s.TokenHash, out _)));
    }

    private static UserSession Revoked(UserSession s, DateTimeOffset now) => new()
    {
        Id = s.Id, UserId = s.UserId, TokenHash = s.TokenHash, CreatedAt = s.CreatedAt,
        LastSeenAt = s.LastSeenAt, ExpiresAt = s.ExpiresAt, RevokedAt = s.RevokedAt ?? now, UserAgent = s.UserAgent, IpAddress = s.IpAddress,
    };

    private void Update(string tokenHash, Func<UserSession, UserSession> change)
    {
        if (sessions.TryGetValue(tokenHash, out var current))
        {
            sessions[tokenHash] = change(current);
        }
    }
}
