using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;

namespace Beeps.Features.Accounts;

/// <summary>
/// Signing in and up with Google or GitHub, and linking them in the settings. The provider vouches for
/// the address, so an account made this way is verified at once, has no password, and was never asked for Turnstile.
/// </summary>
public sealed partial class AccountService
{
    /// <summary>
    /// The account the provider's is linked to. One not linked yet is linked to the account with its verified address,
    /// when that account verified it too; otherwise the person signs up (<see cref="RegisterExternalAsync"/>) with the
    /// username this suggests.
    /// </summary>
    public async Task<Result<ExternalSignIn>> ExternalSignInAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var provider = ExternalProviders.DisplayName(identity.Provider);
        if (await store.FindLoginAsync(identity.Provider, identity.Key, cancellationToken) is { } login)
        {
            var owner = await store.FindUserAsync(login.UserId, cancellationToken);
            if (owner is null)
            {
                return Error.NotFound(ErrorCodes.NotFound, localizer["Error.ExternalAccountGone", provider]);
            }

            return owner.BannedAt is null ? SignedIn(owner) : Banned();
        }

        if (identity.Email is null)
        {
            return NoVerifiedEmail(provider);
        }

        if (await store.FindUserByEmailAsync(AccountRules.NormalizeEmail(identity.Email), cancellationToken) is { } user)
        {
            // An address never verified here may have been typed in by someone else, who set the password: linking it
            // would hand them the provider's sign-in.
            if (user.EmailVerifiedAt is null)
            {
                return ExternalEmailTaken(provider);
            }

            if (user.BannedAt is not null)
            {
                return Banned();
            }

            var linked = await LinkAsync(user, identity, cancellationToken);
            if (!linked.IsSuccess)
            {
                return linked.Error;
            }

            return SignedIn(user);
        }

        if (await store.GetRegistrationModeAsync(cancellationToken) == RegistrationMode.Closed)
        {
            return RegistrationClosed();
        }

        if (disposableEmails.IsDisposable(AccountRules.NormalizeEmail(identity.Email)))
        {
            return DisposableEmail(provider);
        }

        return new ExternalSignIn(null, await SuggestUsernameAsync(identity, cancellationToken));
    }

    /// <summary>
    /// An account for the provider's: only a username to choose (and the invite, when invites are needed). Verified
    /// already, it goes straight on to making an agent.
    /// </summary>
    public async Task<Result<AccountState>> RegisterExternalAsync(ExternalIdentity identity, string? username, string? inviteCode, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(origin);
        var provider = ExternalProviders.DisplayName(identity.Provider);
        if (UsernameError(username) is { } usernameError)
        {
            return InvalidInput(new() { [AccountFields.Username] = [usernameError] });
        }

        if (identity.Email is null)
        {
            return NoVerifiedEmail(provider);
        }

        if (disposableEmails.IsDisposable(AccountRules.NormalizeEmail(identity.Email)))
        {
            return DisposableEmail(provider);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Register, RateLimitPartitions.Ip(origin.ClientIp), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        var mode = await store.GetRegistrationModeAsync(cancellationToken);
        if (mode == RegistrationMode.Closed)
        {
            return RegistrationClosed();
        }

        if (mode == RegistrationMode.InviteOnly && string.IsNullOrWhiteSpace(inviteCode))
        {
            return InvalidInput(new() { [AccountFields.InviteCode] = [localizer["Validation.InviteRequired"]] });
        }

        if (await store.HandleTakenAsync(AccountRules.NormalizeHandle(username!), cancellationToken))
        {
            return UsernameTaken();
        }

        // Signed up meanwhile, in another tab, or the address has an account to sign in to.
        if (await store.FindLoginAsync(identity.Provider, identity.Key, cancellationToken) is not null
            || await store.EmailTakenAsync(AccountRules.NormalizeEmail(identity.Email), cancellationToken))
        {
            return ExternalEmailTaken(provider);
        }

        var now = time.GetUtcNow();
        if (mode == RegistrationMode.InviteOnly && !await store.IsInviteUsableAsync(InviteCodes.Hash(inviteCode!), now, cancellationToken))
        {
            return InviteInvalid();
        }

        var user = NewUser(username!, ProfileRules.CleanDisplayName(identity.Name) ?? username!, identity.Email, origin, now);
        user.EmailVerifiedAt = now;
        try
        {
            return await CreateAccountAsync(
                user,
                mode,
                inviteCode,
                _ =>
                {
                    store.AddLogin(NewLogin(user.Id, identity, now));
                    // Verified, so searchable from the start.
                    outbox.SyncAccountToSearch(user.Id);
                    return Task.CompletedTask;
                },
                () => ExternalEmailTaken(provider),
                cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueLogin)
        {
            return ExternalEmailTaken(provider);
        }
    }

    /// <summary>Links the provider's account from the settings, for signing in with it from now on.</summary>
    public async Task<Result> LinkExternalAsync(Guid userId, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
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

        if (await store.FindLoginAsync(identity.Provider, identity.Key, cancellationToken) is { } login)
        {
            return login.UserId == userId
                ? Result.Success
                : Error.Conflict(AccountErrorCodes.ExternalLinked, localizer["Error.ExternalLinkedElsewhere", ExternalProviders.DisplayName(identity.Provider)]);
        }

        return await LinkAsync(user, identity, cancellationToken);
    }

    /// <summary>
    /// Stops signing in with the provider. Refused for the last way left to sign in, until a password is set (or a
    /// passkey added that signs in by itself).
    /// </summary>
    public async Task<Result> UnlinkExternalAsync(Guid userId, string? provider, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var logins = await store.ListLoginsAsync(userId, cancellationToken);
        if (logins.FirstOrDefault(l => l.Provider == ExternalProviders.Find(provider)) is not { } login)
        {
            return Result.Success;
        }

        // Passkeys sign in by themselves unless they are only the second step.
        if (user.PasswordHash is null && logins.Count == 1
            && (user.PasskeyTwoFactor || (await store.ListPasskeysAsync(userId, cancellationToken)).Count == 0))
        {
            return Error.Conflict(AccountErrorCodes.LastSignInMethod, localizer["Error.LastSignInMethod", ExternalProviders.DisplayName(login.Provider)]);
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        store.RemoveLogin(login);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogUnlinked(logger, userId, login.Provider);
        return Result.Success;
    }

    private static ExternalLogin NewLogin(Guid userId, ExternalIdentity identity, DateTimeOffset now) => new()
    {
        UserId = userId,
        Provider = identity.Provider,
        ProviderKey = identity.Key,
        Name = identity.Shown.Length > ExternalLogin.NameMaxLength ? identity.Shown[..ExternalLogin.NameMaxLength] : identity.Shown,
        CreatedAt = now,
    };

    /// <summary>Links the provider's account, unless the account has another of the same provider.</summary>
    private async Task<Result> LinkAsync(User user, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var provider = ExternalProviders.DisplayName(identity.Provider);
        var other = Error.Conflict(AccountErrorCodes.ExternalLinked, localizer["Error.ExternalProviderTaken", provider]);
        if ((await store.ListLoginsAsync(user.Id, cancellationToken)).Any(l => l.Provider == identity.Provider))
        {
            return other;
        }

        store.AddLogin(NewLogin(user.Id, identity, time.GetUtcNow()));
        NotifySignInMethodAdded(user, provider);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueLoginProvider)
        {
            return other;
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueLogin)
        {
            return Error.Conflict(AccountErrorCodes.ExternalLinked, localizer["Error.ExternalLinkedElsewhere", provider]);
        }

        LogLinked(logger, user.Id, identity.Provider);
        return Result.Success;
    }

    /// <summary>
    /// Stages the notice that a way to sign in was added, or two-step sign-in switched on (<see cref="SignInMethodEmail"/>);
    /// the caller saves.
    /// </summary>
    private void NotifySignInMethodAdded(User user, string? provider, bool twoFactor = false) =>
        outbox.Enqueue(
            SignInMethodEmail.MessageType,
            new SignInMethodEmail(user.Email, user.Locale, provider, twoFactor),
            AccountsJsonContext.Default.SignInMethodEmail);

    /// <summary>
    /// The provider's username, or the address's local part, in the characters a username may have; with a number
    /// after it when someone has it already. Only a suggestion: the form says if it is taken by the time it is sent.
    /// </summary>
    private async Task<string> SuggestUsernameAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        var source = identity.Username ?? identity.Email?.Split('@')[0] ?? "";
        var cleaned = new string([.. source.Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_')]).Trim('_');
        var name = cleaned.Length > AccountRules.UsernameMaxLength - 2 ? cleaned[..(AccountRules.UsernameMaxLength - 2)] : cleaned;
        if (name.Length < AccountRules.UsernameMinLength)
        {
            return name;
        }

        for (var n = 1; n < 10; n++)
        {
            var candidate = n == 1 ? name : $"{name}{n}";
            if (UsernameError(candidate) is null && !await store.HandleTakenAsync(AccountRules.NormalizeHandle(candidate), cancellationToken))
            {
                return candidate;
            }
        }

        return name;
    }

    private static Result<ExternalSignIn> SignedIn(User user) => new ExternalSignIn(ToState(user), null);

    private Error NoVerifiedEmail(string provider) => Error.Validation(AccountErrorCodes.ExternalNoEmail, localizer["Error.ExternalNoEmail", provider]);

    private Error DisposableEmail(string provider) => Error.Validation(AccountErrorCodes.DisposableEmail, localizer["Error.ExternalDisposableEmail", provider]);

    private Error ExternalEmailTaken(string provider) => Error.Conflict(AccountErrorCodes.EmailTaken, localizer["Error.ExternalEmailTaken", provider]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} linked {Provider}")]
    private static partial void LogLinked(ILogger logger, Guid userId, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} unlinked {Provider}")]
    private static partial void LogUnlinked(ILogger logger, Guid userId, string provider);
}
