using Beeps.Features.Live;

namespace Beeps.Features.Accounts;

/// <summary>
/// Server-side sessions for signed-in humans (PLAN 6.14). A request normally costs two Valkey reads
/// (session, account state) and no database query; the database is only read on a cache miss.
/// </summary>
public sealed class SessionService(
    ISessionStore sessions,
    IAccountStore accounts,
    Shared.IUnitOfWork unitOfWork,
    LiveUpdates live,
    TimeProvider time)
{
    /// <summary>A session ends after this long without being used.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Activity is written at most this often, so browsing does not write on every request.</summary>
    public static readonly TimeSpan TouchResolution = TimeSpan.FromHours(1);

    /// <returns>The token for the cookie. It is not stored anywhere on the server, only its hash.</returns>
    public async Task<string> StartAsync(Guid userId, SessionClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var now = time.GetUtcNow();
        var token = SessionTokens.Generate();
        sessions.Add(new UserSession
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            TokenHash = SessionTokens.Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + Lifetime,
            UserAgent = Truncate(client.UserAgent, UserSession.UserAgentMaxLength),
            IpAddress = Truncate(client.IpAddress, UserSession.IpAddressMaxLength),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return token;
    }

    /// <summary>The live session for a cookie token, or null: unknown, revoked, expired, or the account is banned or deleted.</summary>
    public async Task<SignedInSession?> ResolveAsync(string? token, CancellationToken cancellationToken)
    {
        if (!SessionTokens.IsWellFormed(token))
        {
            return null;
        }

        var session = await sessions.FindAsync(SessionTokens.Hash(token!), cancellationToken);
        var now = time.GetUtcNow();
        if (session is null || session.ExpiresAt <= now)
        {
            return null;
        }

        var account = await accounts.GetAccountStateAsync(session.UserId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (now - session.LastSeenAt < TouchResolution)
        {
            return new SignedInSession(session.Id, account, session.ExpiresAt, Renewed: false);
        }

        var expiresAt = now + Lifetime;
        await sessions.TouchAsync(session, now, expiresAt, cancellationToken);
        return new SignedInSession(session.Id, account, expiresAt, Renewed: true);
    }

    /// <summary>Sign-out: the token stops working everywhere at once, and the session's other tabs sign out too.</summary>
    public async Task EndAsync(string? token, CancellationToken cancellationToken)
    {
        if (!SessionTokens.IsWellFormed(token))
        {
            return;
        }

        var hash = SessionTokens.Hash(token!);
        var session = await sessions.FindAsync(hash, cancellationToken);
        await sessions.RevokeAsync(hash, time.GetUtcNow(), cancellationToken);
        if (session is not null)
        {
            await live.SessionsEndedAsync(session.UserId, cancellationToken);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}
