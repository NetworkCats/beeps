using System.Globalization;
using System.Security.Cryptography;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.DataProtection;

namespace Beeps.Features.Accounts;

/// <summary>
/// Passkeys (PLAN 6.14): added in the settings, they sign in without a password, from a button on the login page that
/// asks the browser for any passkey the person has here. An account can make them the second step instead, after the
/// password or Google and GitHub; they then no longer sign in by themselves.
/// </summary>
/// <remarks>
/// Each ceremony is two requests: the options the browser hands the authenticator, then its answer. What the answer is
/// checked against travels in between as a ticket (<see cref="PasskeyRequest.Ticket"/>): encrypted, for that ceremony
/// (and account) alone, and only good for <see cref="PasskeyTicketLifetime"/>. So does the proof that the first step of
/// a two-step sign-in was passed (<see cref="StartSecondStepAsync"/>).
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>Plenty for every device and password manager someone has.</summary>
    public const int MaxPasskeys = 20;

    /// <summary>Long enough for the browser's dialog (5 minutes, <c>IdentityPasskeyOptions.AuthenticatorTimeout</c>) and some.</summary>
    public static readonly TimeSpan PasskeyTicketLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long after the password the passkey may follow, as long as a code sent to sign in lasts.</summary>
    public static readonly TimeSpan SecondStepLifetime = CodeLifetime;

    private const string PasskeyProtectorPurpose = "Beeps.Passkeys";

    /// <summary>The account's passkeys, oldest first, as the settings list them.</summary>
    public async Task<IReadOnlyList<PasskeySummary>> ListPasskeysAsync(Guid userId, CancellationToken cancellationToken) =>
        [.. (await store.ListPasskeysAsync(userId, cancellationToken)).Select(p => new PasskeySummary(p.Id, UserAgents.Describe(p.UserAgent), p.CreatedAt, p.LastUsedAt))];

    /// <summary>The first half of adding a passkey: what the browser asks the authenticator to make.</summary>
    public async Task<Result<PasskeyRequest>> BeginAddPasskeyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var passkeys = await store.ListPasskeysAsync(userId, cancellationToken);
        if (passkeys.Count >= MaxPasskeys)
        {
            return PasskeysFull();
        }

        var challenge = await passkeyCeremonies.BeginRegistrationAsync(user.Id, user.Actor.Handle!, user.Actor.DisplayName!, passkeys);
        return new PasskeyRequest(challenge.OptionsJson, ProtectTicket(PasskeyTicket.Add, user.Id, challenge.State));
    }

    /// <summary>The second half: keeps the passkey the authenticator made, named after where it was added.</summary>
    /// <param name="ticket">What <see cref="BeginAddPasskeyAsync"/> gave, for this account.</param>
    public async Task<Result> AddPasskeyAsync(Guid userId, string? credentialJson, string? ticket, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credentialJson) || UnprotectTicket(PasskeyTicket.Add, userId, ticket) is not { } state)
        {
            return PasskeyFailed();
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        if (await passkeyCeremonies.FinishRegistrationAsync(credentialJson, state, cancellationToken) is not { } created)
        {
            return PasskeyFailed();
        }

        // The same answer sent twice (a second click, the form sent again) adds it once.
        if (await store.FindPasskeyAsync(created.CredentialId, cancellationToken) is { } existing)
        {
            return existing.UserId == userId ? Result.Success : PasskeyFailed();
        }

        if ((await store.ListPasskeysAsync(userId, cancellationToken)).Count >= MaxPasskeys)
        {
            return PasskeysFull();
        }

        var now = time.GetUtcNow();
        store.AddPasskey(new Passkey
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            CredentialId = created.CredentialId,
            PublicKey = created.PublicKey,
            SignCount = created.SignCount,
            Transports = created.Transports,
            BackupEligible = created.BackupEligible,
            BackedUp = created.BackedUp,
            UserAgent = userAgent is { Length: > Passkey.UserAgentMaxLength } ? userAgent[..Passkey.UserAgentMaxLength] : userAgent,
            CreatedAt = now,
        });
        NotifySignInMethodAdded(user, provider: null);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniquePasskey)
        {
            return PasskeyFailed();
        }

        LogPasskeyAdded(logger, userId);
        return Result.Success;
    }

    /// <summary>
    /// Stops signing in with the passkey (it stays on the authenticator, which the settings say). Refused for the last
    /// way left to sign in; the last one also ends two-step sign-in, which has nothing to ask for any more.
    /// </summary>
    public async Task<Result> RemovePasskeyAsync(Guid userId, Guid passkeyId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var passkeys = await store.ListPasskeysAsync(userId, cancellationToken);
        if (passkeys.FirstOrDefault(p => p.Id == passkeyId) is not { } passkey)
        {
            return Result.Success;
        }

        if (user.PasswordHash is null && !user.PasskeyTwoFactor && passkeys.Count == 1
            && (await store.ListLoginsAsync(userId, cancellationToken)).Count == 0)
        {
            return Error.Conflict(AccountErrorCodes.LastSignInMethod, localizer["Error.LastPasskey"]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        store.RemovePasskey(passkey);
        if (passkeys.Count == 1)
        {
            user.PasskeyTwoFactor = false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogPasskeyRemoved(logger, userId);
        return Result.Success;
    }

    /// <summary>
    /// Whether the passkeys sign in by themselves (off) or follow the password, or Google and GitHub (on). On needs a
    /// passkey to ask for, and a first step to follow: a password or a linked provider.
    /// </summary>
    public async Task<Result> SetPasskeyTwoFactorAsync(Guid userId, bool on, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (user.PasskeyTwoFactor == on)
        {
            return Result.Success;
        }

        if (on && (await store.ListPasskeysAsync(userId, cancellationToken)).Count == 0)
        {
            return Error.Conflict(AccountErrorCodes.NoPasskeys, localizer["Error.NoPasskeys"]);
        }

        if (on && user.PasswordHash is null && (await store.ListLoginsAsync(userId, cancellationToken)).Count == 0)
        {
            return Error.Conflict(AccountErrorCodes.LastSignInMethod, localizer["Error.PasskeyTwoFactorFirstStep"]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        user.PasskeyTwoFactor = on;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogPasskeyTwoFactor(logger, userId, on);
        return Result.Success;
    }

    /// <summary>The login page's passkey button, first half: the browser offers whichever passkey the person has here.</summary>
    public async Task<PasskeyRequest> BeginPasskeySignInAsync()
    {
        var challenge = await passkeyCeremonies.BeginAssertionAsync(null, []);
        return new PasskeyRequest(challenge.OptionsJson, ProtectTicket(PasskeyTicket.SignIn, null, challenge.State));
    }

    /// <summary>
    /// The second half: signs in to the account whose passkey signed. Attempts from one IP are limited as password
    /// guesses are. An account that keeps its passkeys for the second step is not signed in this way.
    /// </summary>
    public async Task<Result<AccountState>> PasskeySignInAsync(string? credentialJson, string? ticket, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Login, RateLimitPartitions.Ip(origin.ClientIp), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        if (ReadTicket(PasskeyTicket.SignIn, null, ticket) is not { } challenge)
        {
            return PasskeyFailed();
        }

        var signed = await CheckPasskeyAsync(credentialJson, challenge, null, cancellationToken);
        if (!signed.IsSuccess)
        {
            return signed.Error;
        }

        var (user, passkey, use) = signed.Value;
        if (user.PasskeyTwoFactor)
        {
            return Error.Forbidden(AccountErrorCodes.PasskeyTwoFactor, localizer["Error.PasskeyTwoFactor"]);
        }

        await RecordPasskeyUseAsync(passkey, use, cancellationToken);
        return ToState(user);
    }

    /// <summary>
    /// After the first step of signing in (the password, a code, Google or GitHub): the proof it was passed, for the
    /// passkey to follow, if the account asks for one; null if it does not, and the account is signed in as it is.
    /// </summary>
    public async Task<string?> StartSecondStepAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null || !user.PasskeyTwoFactor || (await store.ListPasskeysAsync(userId, cancellationToken)).Count == 0)
        {
            return null;
        }

        return ProtectTicket(PasskeyTicket.FirstStep, null, user.Id.ToString("D"));
    }

    /// <summary>The second step, first half: the browser asks for one of the account's passkeys.</summary>
    /// <param name="firstStep">What <see cref="StartSecondStepAsync"/> gave.</param>
    public async Task<Result<PasskeyRequest>> BeginSecondStepAsync(string? firstStep, CancellationToken cancellationToken)
    {
        if (FirstStepUser(firstStep) is not { } userId)
        {
            return SecondStepExpired();
        }

        var passkeys = await store.ListPasskeysAsync(userId, cancellationToken);
        if (passkeys.Count == 0)
        {
            return SecondStepExpired();
        }

        var challenge = await passkeyCeremonies.BeginAssertionAsync(userId, passkeys);
        return new PasskeyRequest(challenge.OptionsJson, ProtectTicket(PasskeyTicket.SecondStep, userId, challenge.State));
    }

    /// <summary>The second half: signs in, once one of the account's own passkeys signed. Attempts are limited per account.</summary>
    public async Task<Result<AccountState>> FinishSecondStepAsync(string? firstStep, string? credentialJson, string? ticket, CancellationToken cancellationToken)
    {
        if (FirstStepUser(firstStep) is not { } userId)
        {
            return SecondStepExpired();
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Login, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        if (ReadTicket(PasskeyTicket.SecondStep, userId, ticket) is not { } challenge)
        {
            return PasskeyFailed();
        }

        var signed = await CheckPasskeyAsync(credentialJson, challenge, userId, cancellationToken);
        if (!signed.IsSuccess)
        {
            return signed.Error;
        }

        var (user, passkey, use) = signed.Value;
        await RecordPasskeyUseAsync(passkey, use, cancellationToken);
        return ToState(user);
    }

    /// <summary>
    /// The passkey the browser's answer names, its account, and what the authenticator said, if the answer holds up for
    /// it. Refused for an account that is gone or banned, and, with <paramref name="userId"/>, for anyone else's passkey.
    /// </summary>
    private async Task<Result<(User User, Passkey Passkey, PasskeyUse Use)>> CheckPasskeyAsync(
        string? credentialJson, Ticket challenge, Guid? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(credentialJson)
            || passkeyCeremonies.CredentialIdOf(credentialJson) is not { } credentialId
            || await store.FindPasskeyAsync(credentialId, cancellationToken) is not { } passkey
            || (userId is not null && passkey.UserId != userId))
        {
            return PasskeyFailed();
        }

        // A challenge is answered once: the same answer sent again (from a log, say) is refused, since the passkey has
        // signed in since the challenge was made. Synced passkeys keep their sign count at 0, so it cannot tell.
        if (passkey.LastUsedAt > challenge.IssuedAt)
        {
            return PasskeyFailed();
        }

        if (await passkeyCeremonies.FinishAssertionAsync(credentialJson, challenge.Payload, passkey, cancellationToken) is not { } use)
        {
            return PasskeyFailed();
        }

        var user = await store.FindUserAsync(passkey.UserId, cancellationToken);
        if (user is null)
        {
            return PasskeyFailed();
        }

        return user.BannedAt is null ? (user, passkey, use) : Banned();
    }

    private async Task RecordPasskeyUseAsync(Passkey passkey, PasskeyUse use, CancellationToken cancellationToken)
    {
        passkey.SignCount = use.SignCount;
        passkey.BackedUp = use.BackedUp;
        passkey.LastUsedAt = time.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The account whose first step this proves, while it is fresh enough.</summary>
    private Guid? FirstStepUser(string? firstStep) =>
        Guid.TryParseExact(UnprotectTicket(PasskeyTicket.FirstStep, null, firstStep), "D", out var userId) ? userId : null;

    /// <summary>
    /// Encrypts <paramref name="payload"/> for one sort of ticket (and one account), with when it stops being good. The
    /// time is the site's clock (<see cref="TimeProvider"/>), not the time-limited protector's, which tests cannot set.
    /// </summary>
    private string ProtectTicket(PasskeyTicket kind, Guid? userId, string payload)
    {
        var expires = (time.GetUtcNow() + TicketLifetime(kind)).ToUnixTimeMilliseconds();
        return TicketProtector(kind, userId).Protect($"{expires.ToString(CultureInfo.InvariantCulture)}\n{payload}");
    }

    /// <summary>What a ticket of this sort (and account) carries; null if it is not one, or is too old.</summary>
    private string? UnprotectTicket(PasskeyTicket kind, Guid? userId, string? ticket) => ReadTicket(kind, userId, ticket)?.Payload;

    /// <summary>What a ticket of this sort (and account) carries, and when it was made; null if it is not one, or is too old.</summary>
    private Ticket? ReadTicket(PasskeyTicket kind, Guid? userId, string? ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        string text;
        try
        {
            text = TicketProtector(kind, userId).Unprotect(ticket);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var split = text.IndexOf('\n', StringComparison.Ordinal);
        return split > 0
            && long.TryParse(text.AsSpan(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            && time.GetUtcNow().ToUnixTimeMilliseconds() < expires
                ? new Ticket(text[(split + 1)..], DateTimeOffset.FromUnixTimeMilliseconds(expires) - TicketLifetime(kind))
                : null;
    }

    private static TimeSpan TicketLifetime(PasskeyTicket kind) => kind == PasskeyTicket.FirstStep ? SecondStepLifetime : PasskeyTicketLifetime;

    private IDataProtector TicketProtector(PasskeyTicket kind, Guid? userId) =>
        userId is { } id
            ? dataProtection.CreateProtector(PasskeyProtectorPurpose, kind.ToString(), id.ToString("D"))
            : dataProtection.CreateProtector(PasskeyProtectorPurpose, kind.ToString());

    private Error PasskeyFailed() => Error.Validation(AccountErrorCodes.PasskeyFailed, localizer["Error.PasskeyFailed"]);

    private Error PasskeysFull() => Error.Conflict(AccountErrorCodes.PasskeysFull, localizer["Error.PasskeysFull", MaxPasskeys]);

    private Error SecondStepExpired() => Error.Validation(AccountErrorCodes.PasskeyFailed, localizer["Error.SecondStepExpired"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} added a passkey")]
    private static partial void LogPasskeyAdded(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} removed a passkey")]
    private static partial void LogPasskeyRemoved(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} set two-step sign-in with passkeys to {On}")]
    private static partial void LogPasskeyTwoFactor(ILogger logger, Guid userId, bool on);

    private sealed record Ticket(string Payload, DateTimeOffset IssuedAt);

    /// <summary>What a ticket stands for: each sort is encrypted for itself, so none passes for another.</summary>
    private enum PasskeyTicket
    {
        /// <summary>Adding a passkey, for one account.</summary>
        Add,

        /// <summary>Signing in with any passkey, from the login page.</summary>
        SignIn,

        /// <summary>The first step of a two-step sign-in was passed; carries the account's id.</summary>
        FirstStep,

        /// <summary>The second step, for one account.</summary>
        SecondStep,
    }
}
