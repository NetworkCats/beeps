using System.Globalization;
using Beeps.Features.Live;
using Beeps.Features.Localization;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>
/// Server-side sessions for signed-in humans (PLAN 6.14). A request normally costs two Valkey reads
/// (session, account state) and no database query; the database is only read on a cache miss. The account's settings
/// list its sessions as devices, and end any of them.
/// </summary>
public sealed class SessionService(
    ISessionStore sessions,
    IAccountStore accounts,
    Shared.IUnitOfWork unitOfWork,
    IIpLocations locations,
    LiveUpdates live,
    IStringLocalizer<SharedResource> localizer,
    TimeProvider time)
{
    /// <summary>A session ends after this long without being used.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Activity is written at most this often, so browsing does not write on every request.</summary>
    public static readonly TimeSpan TouchResolution = TimeSpan.FromHours(1);

    /// <summary>The devices page lists at most this many; signing out everywhere else ends the rest too.</summary>
    public const int DeviceListLimit = 50;

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
        // The account's devices page lists the new one.
        await live.SessionStartedAsync(userId, cancellationToken);
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

    /// <summary>
    /// The account's signed-in devices: <paramref name="currentSessionId"/> first, then the most recently used. Each is
    /// named from the User-Agent it signed in with and placed where its IP address was, in the page language.
    /// </summary>
    public async Task<IReadOnlyList<SignedInDevice>> ListDevicesAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var listed = await sessions.ListActiveAsync(userId, currentSessionId, time.GetUtcNow(), DeviceListLimit, cancellationToken);
        return [.. listed.Select(s => new SignedInDevice(
            s.Id,
            UserAgents.Describe(s.UserAgent),
            Place(locations.Find(s.IpAddress, CultureInfo.CurrentUICulture)),
            s.CreatedAt,
            s.LastSeenAt,
            s.Id == currentSessionId))];
    }

    /// <summary>
    /// Signs one of the account's other devices out: its tabs sign out at once. One already gone is no error, it is
    /// what was asked for. The device this is asked from signs out the usual way.
    /// </summary>
    public async Task<Result> EndDeviceAsync(Guid userId, Guid? currentSessionId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == currentSessionId)
        {
            return Error.Validation(AccountErrorCodes.InvalidInput, localizer["Sessions.Error.Current"]);
        }

        if (await sessions.RevokeByIdAsync(userId, sessionId, time.GetUtcNow(), cancellationToken))
        {
            await live.SessionsEndedAsync(userId, cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>Signs every device out but the one this is asked from, which needs no password: it is signed in already.</summary>
    /// <param name="currentSessionId">The session to keep; without one there is nothing to keep, and nothing ends.</param>
    public async Task<Result> EndOtherDevicesAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        if (currentSessionId is not { } keep)
        {
            return Error.Validation(AccountErrorCodes.InvalidInput, localizer["Sessions.Error.Current"]);
        }

        await sessions.RevokeOthersAsync(userId, keep, time.GetUtcNow(), cancellationToken);
        await sessions.ForgetAllAsync(userId);
        await live.SessionsEndedAsync(userId, cancellationToken);
        return Result.Success;
    }

    /// <summary>The town (or, without one, the region) and the country, leaving out a town named as its country (Singapore).</summary>
    private static DevicePlace? Place(IpLocation? location)
    {
        if (location is null)
        {
            return null;
        }

        var locality = location.City ?? location.Region;
        if (locality is not null && string.Equals(locality, location.Country, StringComparison.OrdinalIgnoreCase))
        {
            locality = null;
        }

        return locality is null && location.Country is null ? null : new DevicePlace(locality, location.Country);
    }

    private static string? Truncate(string? value, int maxLength) => value is null ? null : TextRules.Truncate(value, maxLength);
}
