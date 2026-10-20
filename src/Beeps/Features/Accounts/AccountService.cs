using System.Globalization;
using System.Security.Cryptography;
using Beeps.Features.Live;
using Beeps.Features.Localization;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Site;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>Human accounts: registration, email codes, sign-in, password reset, email changes and passkeys (PLAN 3.1, 6.13–6.15).</summary>
public sealed partial class AccountService(
    IAccountStore store,
    ISessionStore sessions,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    ICaptchaVerifier captcha,
    IPasswordHasher<User> passwordHasher,
    IPasskeyCeremonies passkeyCeremonies,
    RateLimitService rateLimits,
    LiveUpdates live,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<AccountService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public const int MaxCodeAttempts = 5;

    /// <summary>The least time between two codes for the same thing (PLAN 6.13); the pages count it down.</summary>
    public static readonly TimeSpan ResendInterval = TimeSpan.FromMinutes(1);

    private static readonly User TimingDummy = new()
    {
        Email = "",
        NormalizedEmail = "",
        PasswordHash = "",
        Locale = SupportedCultures.Default,
    };

    private static string? timingDummyHash;

    private readonly IDataProtector codeProtector = dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose);

    /// <summary>
    /// Creates the account and sends the verification code. The very first account becomes admin:
    /// the check and the insert share one transaction under <see cref="AdvisoryLocks.Admins"/>, so two
    /// simultaneous first sign-ups cannot both win.
    /// </summary>
    public async Task<Result<AccountState>> RegisterAsync(RegisterRequest request, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(origin);
        var fields = new Dictionary<string, string[]>();
        if (!AccountRules.IsValidEmail(request.Email?.Trim()))
        {
            fields[AccountFields.Email] = [localizer["Validation.Email"]];
        }

        if (UsernameError(request.Username) is { } usernameError)
        {
            fields[AccountFields.Username] = [usernameError];
        }

        if (!AccountRules.IsValidPassword(request.Password))
        {
            fields[AccountFields.Password] = [localizer["Validation.Password", AccountRules.PasswordMinLength, AccountRules.PasswordMaxLength]];
        }

        if (fields.Count > 0)
        {
            return InvalidInput(fields);
        }

        if (!await captcha.VerifyAsync(request.CaptchaToken, origin.ClientIp, cancellationToken))
        {
            return Error.Validation(AccountErrorCodes.CaptchaFailed, localizer["Error.CaptchaFailed"]);
        }

        var email = AccountRules.NormalizeEmail(request.Email!);
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

        if (mode == RegistrationMode.InviteOnly && string.IsNullOrWhiteSpace(request.InviteCode))
        {
            return InvalidInput(new() { [AccountFields.InviteCode] = [localizer["Validation.InviteRequired"]] });
        }

        var handle = request.Username!;
        var normalizedHandle = AccountRules.NormalizeHandle(handle);
        if (await store.HandleTakenAsync(normalizedHandle, cancellationToken))
        {
            return UsernameTaken();
        }

        if (await store.EmailTakenAsync(email, cancellationToken))
        {
            return EmailTaken();
        }

        var now = time.GetUtcNow();
        if (mode == RegistrationMode.InviteOnly && !await store.IsInviteUsableAsync(InviteCodes.Hash(request.InviteCode!), now, cancellationToken))
        {
            return InviteInvalid();
        }

        // Counted only now that a code email is certain, so a rejected attempt does not block the retry.
        var emailLimited = await rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, RateLimitPartitions.Ip(origin.ClientIp)),
                new RateLimitTarget(RateLimitPolicies.EmailCode, RateLimitPartitions.Email(email)),
            ],
            cancellationToken);
        if (!emailLimited.IsSuccess)
        {
            return emailLimited.Error;
        }

        var user = NewUser(handle, handle, request.Email!.Trim(), origin, now);
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        return await CreateAccountAsync(user, mode, request.InviteCode, ct => IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, ct), EmailTaken, cancellationToken);
    }

    public async Task<Result> VerifyEmailAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsWellFormedCode(code))
        {
            return CodeInvalid();
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (user.EmailVerifiedAt is not null)
        {
            return Result.Success;
        }

        var email = user.NormalizedEmail;
        var consumed = await ConsumeCodeAsync(EmailCodePurpose.VerifyEmail, email, code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        // A move to another address sent alongside must not leave that one verified by this code.
        var verified = await unitOfWork.InTransactionAsync(
            AdvisoryLocks.UnverifiedEmail(user.Id),
            async ct =>
            {
                if (await store.LeftUnverifiedEmailAsync(user.Id, email, ct))
                {
                    return CodeInvalid();
                }

                user.EmailVerifiedAt = time.GetUtcNow();
                // From now on the person can be found by search.
                outbox.SyncAccountToSearch(user.Id);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (!verified.IsSuccess)
        {
            return verified;
        }

        await store.ForgetAccountAsync(user.Id, cancellationToken);
        // Their other open tabs become a member's pages; the admin user list drops the "unverified" mark.
        await live.AccountChangedAsync(user.Id, cancellationToken);
        await live.AdminChangedAsync(cancellationToken);
        return Result.Success;
    }

    public async Task<Result> ResendVerificationAsync(Guid userId, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (user.EmailVerifiedAt is not null)
        {
            return AlreadyVerified();
        }

        var tooSoon = TooSoon(await store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, cancellationToken));
        if (!tooSoon.IsSuccess)
        {
            return tooSoon;
        }

        var limited = await CheckCodeLimitsAsync(user, user.NormalizedEmail, origin, cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        await IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    /// <summary>What the verification page shows: where the code went, and when another may be sent.</summary>
    public async Task<Result<Verification>> GetVerificationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var code = await store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, cancellationToken);
        return new Verification(user.Email, code?.CreatedAt + ResendInterval);
    }

    /// <summary>
    /// For an address mistyped at sign-up: the account, not yet verified, moves to another one at once and the
    /// verification code goes there instead. Needs the password, as <see cref="RequestEmailChangeAsync"/> does once
    /// verified. The account keeps its sign-up date, so it is still released if it never verifies (PLAN 6.13).
    /// </summary>
    public async Task<Result> ChangeUnverifiedEmailAsync(Guid userId, string? newEmail, string? currentPassword, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var checkedEmail = await CheckNewEmailAsync(userId, newEmail, currentPassword, cancellationToken);
        if (!checkedEmail.IsSuccess)
        {
            return checkedEmail.Error;
        }

        var (user, email) = checkedEmail.Value;
        if (user.EmailVerifiedAt is not null)
        {
            return AlreadyVerified();
        }

        var limited = await CheckCodeLimitsAsync(user, email, origin, cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        Result moved;
        try
        {
            // The old address may have been verified meanwhile (its code sent alongside): it stays the account's.
            moved = await unitOfWork.InTransactionAsync(
                AdvisoryLocks.UnverifiedEmail(user.Id),
                async ct =>
                {
                    if (await store.LeftUnverifiedEmailAsync(user.Id, user.NormalizedEmail, ct))
                    {
                        return AlreadyVerified();
                    }

                    // The code sent to the old address stops working.
                    if (await store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, ct) is { } old)
                    {
                        store.RemoveEmailCode(old);
                    }

                    user.Email = newEmail!.Trim();
                    user.NormalizedEmail = email;
                    await IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success;
                },
                cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueEmail)
        {
            return NewEmailTaken();
        }

        if (!moved.IsSuccess)
        {
            return moved;
        }

        // The admin user list shows the address.
        await live.AdminChangedAsync(cancellationToken);
        LogEmailChanged(logger, user.Id);
        return Result.Success;
    }

    /// <summary>
    /// Signs in with a password. Guesses from one IP are refused past their limit. Wrong passwords for one account are
    /// counted too, from anywhere, but past their limit the account is not locked, which would let anyone lock anyone
    /// out: signing in to it takes Turnstile first, and then a code sent to its address (<see cref="LoginWithCodeAsync"/>).
    /// </summary>
    public async Task<Result<LoginOutcome>> LoginAsync(LoginRequest request, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(origin);
        var (login, password) = (request.Login, request.Password);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
        {
            var fields = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(login))
            {
                fields[AccountFields.Login] = [localizer["Validation.Required"]];
            }

            if (string.IsNullOrEmpty(password))
            {
                fields[AccountFields.Password] = [localizer["Validation.Required"]];
            }

            return InvalidInput(fields);
        }

        var fromIp = await rateLimits.CheckAsync(RateLimitPolicies.Login, RateLimitPartitions.Ip(origin.ClientIp), cancellationToken);
        if (!fromIp.IsSuccess)
        {
            return fromIp.Error;
        }

        var (user, normalized) = await FindByLoginAsync(login, cancellationToken);
        // An account has one counter of wrong passwords, by address or username alike. A name no account has gets its
        // own, so it is asked for Turnstile just as soon and that does not tell which names exist. Each attempt takes a
        // place on it before the password is checked, and the right password gives it back: attempts sent all at once
        // cannot slip past it before their failures are counted.
        var failures = user is null ? RateLimitPartitions.Login(normalized) : RateLimitPartitions.Account(user.Id);
        var counted = await rateLimits.CheckAsync(RateLimitPolicies.LoginAccount, failures, cancellationToken);
        if (!counted.IsSuccess)
        {
            return await LoginPastFailuresAsync(user, password, request.CaptchaToken, origin, cancellationToken);
        }

        if (user is null)
        {
            // Same work as a wrong password, so response times do not reveal which accounts exist.
            passwordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash(), password);
            return InvalidCredentials();
        }

        if (user.PasswordHash is null)
        {
            // Signs in only with Google or GitHub; told apart from a wrong password by nothing, timing included.
            passwordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash(), password);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        await rateLimits.ReleaseAsync(RateLimitPolicies.LoginAccount, failures, cancellationToken);
        if (user.BannedAt is not null)
        {
            return Banned();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new LoginOutcome(ToState(user));
    }

    /// <summary>
    /// The second step of signing in to an account past its wrong passwords (<see cref="LoginAsync"/>): the code sent
    /// to its address, which only Turnstile and then the right password asked for.
    /// </summary>
    /// <param name="login">The email address or username the first step was given.</param>
    /// <param name="codeStamp">
    /// What the first step answered with (<see cref="LoginOutcome.CodeStamp"/>). Guesses count against the code only
    /// when it names that code, which only the right password gets: anyone can name the account and get past
    /// Turnstile with a wrong password, but must not use up the attempts of the code sent to its owner.
    /// </param>
    public async Task<Result<AccountState>> LoginWithCodeAsync(string? login, string? codeStamp, string? code, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsWellFormedCode(code))
        {
            return InvalidInput(new() { [AccountFields.Code] = [localizer["Validation.Code"]] });
        }

        if (string.IsNullOrWhiteSpace(login) || (await FindByLoginAsync(login, cancellationToken)).User is not { } user)
        {
            return CodeInvalid();
        }

        var issued = await store.FindEmailCodeAsync(EmailCodePurpose.SignIn, user.NormalizedEmail, cancellationToken);
        if (issued is null || codeStamp is null || !TextHashes.FixedTimeEquals(CodeStamp(issued.CreatedAt), codeStamp))
        {
            return CodeInvalid();
        }

        // Every guess counts, right or wrong (ConsumeCodeAsync).
        var consumed = await ConsumeCodeAsync(EmailCodePurpose.SignIn, user.NormalizedEmail, code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return user.BannedAt is null ? ToState(user) : Banned();
    }

    /// <summary>
    /// Sends a reset code if the address belongs to an account. The answer is the same either way, so
    /// the form cannot be used to find out who has an account: a stamp naming the code sent, or nothing, for the
    /// browser that asked to bring back with its guesses (<see cref="ResetPasswordAsync"/>).
    /// </summary>
    public async Task<Result<string>> RequestPasswordResetAsync(string? email, string? captchaToken, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!AccountRules.IsValidEmail(email?.Trim()))
        {
            return InvalidInput(new() { [AccountFields.Email] = [localizer["Validation.Email"]] });
        }

        if (!await captcha.VerifyAsync(captchaToken, origin.ClientIp, cancellationToken))
        {
            return Error.Validation(AccountErrorCodes.CaptchaFailed, localizer["Error.CaptchaFailed"]);
        }

        var normalized = AccountRules.NormalizeEmail(email!);
        var limited = await rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.PasswordReset, RateLimitPartitions.Ip(origin.ClientIp)),
                new RateLimitTarget(RateLimitPolicies.PasswordReset, RateLimitPartitions.Email(normalized)),
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, RateLimitPartitions.Ip(origin.ClientIp)),
                // Not EmailCode: anyone can ask for a reset, and running it up must not stop the codes that signing in sends.
                new RateLimitTarget(RateLimitPolicies.PasswordResetCode, RateLimitPartitions.Email(normalized)),
            ],
            cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        var user = await store.FindUserByEmailAsync(normalized, cancellationToken);
        if (user is null || user.BannedAt is not null)
        {
            return DummyCodeStamp();
        }

        var issued = await IssueCodeAsync(EmailCodePurpose.ResetPassword, user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CodeStamp(issued);
    }

    /// <summary>
    /// Sets the new password and ends every existing session. Guesses count against the code only when the request
    /// brings its stamp (<see cref="RequestPasswordResetAsync"/>), which only the browser that asked for it has:
    /// anyone can name the address, but must not use up the attempts of the code sent to its owner.
    /// </summary>
    public async Task<Result> ResetPasswordAsync(PasswordResetRequest request, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(origin);
        var fields = new Dictionary<string, string[]>();
        if (!AccountRules.IsValidEmail(request.Email?.Trim()))
        {
            fields[AccountFields.Email] = [localizer["Validation.Email"]];
        }

        if (!AccountRules.IsWellFormedCode(request.Code))
        {
            fields[AccountFields.Code] = [localizer["Validation.Code"]];
        }

        if (!AccountRules.IsValidPassword(request.NewPassword))
        {
            fields[AccountFields.Password] = [localizer["Validation.Password", AccountRules.PasswordMinLength, AccountRules.PasswordMaxLength]];
        }

        if (fields.Count > 0)
        {
            return InvalidInput(fields);
        }

        var user = await store.FindUserByEmailAsync(AccountRules.NormalizeEmail(request.Email!), cancellationToken);
        if (user is null)
        {
            return CodeInvalid();
        }

        var issued = await store.FindEmailCodeAsync(EmailCodePurpose.ResetPassword, user.NormalizedEmail, cancellationToken);
        if (issued is null || request.CodeStamp is null || !TextHashes.FixedTimeEquals(CodeStamp(issued.CreatedAt), request.CodeStamp))
        {
            return CodeInvalid();
        }

        // Per account and IP: one who asks for codes to guess at must not use up the guesses of the owner's own reset.
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.PasswordResetGuess, RateLimitPartitions.AccountFromIp(user.Id, origin.ClientIp), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        // Outside the transaction: a wrong guess must stay counted even though nothing else is saved.
        var consumed = await ConsumeCodeAsync(EmailCodePurpose.ResetPassword, user.NormalizedEmail, request.Code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        // The new password and the end of every old session commit together. The code proves the address it went to,
        // unless the account moved to another meanwhile (sent alongside): that one stays unverified.
        var verifies = user.EmailVerifiedAt is null;
        var email = user.NormalizedEmail;
        var result = await unitOfWork.InTransactionAsync(
            verifies ? AdvisoryLocks.UnverifiedEmail(user.Id) : null,
            async ct =>
            {
                var now = time.GetUtcNow();
                user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
                verifies = verifies && !await store.LeftUnverifiedEmailAsync(user.Id, email, ct);
                if (verifies)
                {
                    // The code proved the address works, so from now on the person can be found by search.
                    user.EmailVerifiedAt = now;
                    outbox.SyncAccountToSearch(user.Id);
                }

                await sessions.RevokeAllAsync(user.Id, now, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        await sessions.ForgetAllAsync(user.Id);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
        await live.SessionsEndedAsync(user.Id, cancellationToken);
        if (verifies)
        {
            // The admin user list drops the "unverified" mark.
            await live.AdminChangedAsync(cancellationToken);
        }

        LogPasswordReset(logger, user.Id);
        return Result.Success;
    }

    /// <summary>Remembers the UI language; it then wins over the cookie and the browser (PLAN 6.12).</summary>
    public async Task<Result> SetLocaleAsync(Guid userId, string? culture, CancellationToken cancellationToken)
    {
        if (SupportedCultures.Find(culture) is not { } supported)
        {
            return InvalidInput(new());
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        user.Locale = supported;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
        // The account's other open tabs switch to the new language too.
        await live.AccountChangedAsync(user.Id, cancellationToken);
        return Result.Success;
    }

    /// <summary>The account's colour scheme (<see cref="Themes"/>), on every device it is signed in on.</summary>
    public async Task<Result> SetThemeAsync(Guid userId, string? theme, CancellationToken cancellationToken)
    {
        if (Themes.Find(theme) is not { } supported)
        {
            return InvalidInput(new());
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        user.Theme = supported;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
        // The account's other open tabs switch too, in place: nothing else on them changes.
        await live.ThemeChangedAsync(user.Id, supported, cancellationToken);
        return Result.Success;
    }

    /// <summary>What the settings page shows and edits.</summary>
    public async Task<Result<AccountSettings>> GetSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        // A move whose code has expired or run out of guesses is over: the page asks for an address again.
        var pending = user.PendingEmail is null
            ? null
            : await store.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, user.NormalizedEmail, cancellationToken);
        var waiting = pending is not null && pending.ExpiresAt > time.GetUtcNow() && pending.Attempts < MaxCodeAttempts
            ? new CodeSent(user.PendingEmail!, pending.CreatedAt + ResendInterval)
            : null;
        return new AccountSettings(
            user.Actor.Handle!,
            user.Actor.DisplayName!,
            user.Actor.Bio,
            user.Actor.Website,
            user.Email,
            waiting,
            user.Locale,
            new ProfilePrivacy(user.HideLikes, user.HideFollows, user.HideFollowCounts),
            user.PasswordHash is not null,
            [.. (await store.ListLoginsAsync(user.Id, cancellationToken)).Select(l => new LinkedLogin(l.Provider, l.Name))],
            user.PasskeyTwoFactor);
    }

    /// <summary>What the human's profile shows others of their likes and follows (PLAN 5.1).</summary>
    public async Task<Result> SetPrivacyAsync(Guid userId, ProfilePrivacy privacy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(privacy);
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

        (user.HideLikes, user.HideFollows, user.HideFollowCounts) = (privacy.HideLikes, privacy.HideFollows, privacy.HideFollowCounts);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Open profiles of theirs, here and elsewhere, show more or less of them.
        await live.ActorChangedAsync(user.Id, cancellationToken);
        return Result.Success;
    }

    /// <summary>The human's own display name, bio and website (PLAN 2.2); the username never changes.</summary>
    public async Task<Result> UpdateProfileAsync(Guid userId, string? displayName, string? bio, string? website, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string[]>();
        var name = ProfileRules.CleanDisplayName(displayName);
        if (name is null)
        {
            fields[ProfileFields.DisplayName] = [localizer["Validation.DisplayName", Actor.DisplayNameMaxLength]];
        }

        if (!ProfileRules.TryCleanBio(bio, out var cleanBio))
        {
            fields[ProfileFields.Bio] = [localizer["Validation.Bio", Actor.BioMaxLength]];
        }

        if (!ProfileRules.TryCleanWebsite(website, out var cleanWebsite))
        {
            fields[ProfileFields.Website] = [localizer["Validation.Website", Actor.WebsiteMaxLength]];
        }

        if (fields.Count > 0)
        {
            return InvalidInput(fields);
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        // A write that also reindexes and invalidates caches (PLAN 12, 6.16).
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(userId), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        user.Actor.DisplayName = name;
        user.Actor.Bio = cleanBio;
        user.Actor.Website = cleanWebsite;
        outbox.SyncAccountToSearch(user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
        await live.ActorChangedAsync(user.Id, cancellationToken);
        return Result.Success;
    }

    /// <summary>
    /// Needs the current password. Every other session ends; the one the change was made in stays, since it
    /// just proved it knows the password. Guesses count against the same limit as signing in to the account.
    /// An account that signs in only with Google or GitHub has none to give: this sets its first.
    /// </summary>
    /// <param name="currentSessionId">The session making the change; null ends every session.</param>
    public async Task<Result> ChangePasswordAsync(Guid userId, Guid? currentSessionId, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsValidPassword(newPassword))
        {
            return InvalidInput(new() { [AccountFields.NewPassword] = [localizer["Validation.Password", AccountRules.PasswordMinLength, AccountRules.PasswordMaxLength]] });
        }

        var checkedPassword = await CheckPasswordAsync(userId, currentPassword, cancellationToken);
        if (!checkedPassword.IsSuccess)
        {
            return checkedPassword.Error;
        }

        var user = checkedPassword.Value;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                user.PasswordHash = passwordHasher.HashPassword(user, newPassword!);
                await (currentSessionId is { } keep ? sessions.RevokeOthersAsync(user.Id, keep, now, ct) : sessions.RevokeAllAsync(user.Id, now, ct));
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        await sessions.ForgetAllAsync(user.Id);
        // The other tabs of the sessions just ended sign out.
        await live.SessionsEndedAsync(user.Id, cancellationToken);
        LogPasswordChanged(logger, user.Id);
        return Result.Success;
    }

    /// <summary>
    /// The first step of moving the account to another address: needs the current password, like
    /// <see cref="ChangePasswordAsync"/>, and sends a code to the new address. Nothing changes until it is entered
    /// (<see cref="ConfirmEmailChangeAsync"/>); a new request replaces the one before.
    /// </summary>
    public async Task<Result> RequestEmailChangeAsync(Guid userId, string? newEmail, string? currentPassword, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var checkedEmail = await CheckNewEmailAsync(userId, newEmail, currentPassword, cancellationToken);
        if (!checkedEmail.IsSuccess)
        {
            return checkedEmail.Error;
        }

        var (user, email) = checkedEmail.Value;
        var limited = await CheckCodeLimitsAsync(user, email, origin, cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        // The address and its code are saved together, so a code only ever stands for the address it was sent to.
        user.PendingEmail = newEmail!.Trim();
        await IssueCodeAsync(EmailCodePurpose.ChangeEmail, user, user.PendingEmail, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    /// <summary>Sends the code for the move under way again, to the same address; the password was given when it was asked for.</summary>
    public async Task<Result> ResendEmailChangeAsync(Guid userId, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        // Expired codes count as none: the page then asks for the address and the password again.
        var code = user.PendingEmail is null
            ? null
            : await store.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, user.NormalizedEmail, cancellationToken);
        if (code is null || code.ExpiresAt <= time.GetUtcNow())
        {
            return CodeInvalid();
        }

        var tooSoon = TooSoon(code);
        if (!tooSoon.IsSuccess)
        {
            return tooSoon;
        }

        var limited = await CheckCodeLimitsAsync(user, AccountRules.NormalizeEmail(user.PendingEmail!), origin, cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        await IssueCodeAsync(EmailCodePurpose.ChangeEmail, user, user.PendingEmail!, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    /// <summary>
    /// Moves the account to the address <see cref="RequestEmailChangeAsync"/> sent the code to. The old address is
    /// free from then on; sign-in and password resets use the new one.
    /// </summary>
    public async Task<Result> ConfirmEmailChangeAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsWellFormedCode(code))
        {
            return CodeInvalid();
        }

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (user.PendingEmail is null)
        {
            return CodeInvalid();
        }

        var email = AccountRules.NormalizeEmail(user.PendingEmail);
        // Someone may have registered or moved to it since; the code is kept for nothing then.
        if (await store.EmailTakenAsync(email, cancellationToken))
        {
            return Error.Field(AccountErrorCodes.EmailTaken, AccountFields.Code, localizer["Error.EmailTaken"], ErrorKind.Conflict);
        }

        // Outside the save: a wrong guess must stay counted even though nothing else is saved.
        var consumed = await ConsumeCodeAsync(EmailCodePurpose.ChangeEmail, user.NormalizedEmail, code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        user.Email = user.PendingEmail;
        user.NormalizedEmail = email;
        user.PendingEmail = null;
        // The code proved the address works.
        user.EmailVerifiedAt ??= time.GetUtcNow();
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueEmail)
        {
            return Error.Field(AccountErrorCodes.EmailTaken, AccountFields.Code, localizer["Error.EmailTaken"], ErrorKind.Conflict);
        }

        // The admin user list shows the address.
        await live.AdminChangedAsync(cancellationToken);
        LogEmailChanged(logger, user.Id);
        return Result.Success;
    }

    /// <summary>Drops a move that was asked for: its code no longer works.</summary>
    public async Task<Result> CancelEmailChangeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (await store.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, user.NormalizedEmail, cancellationToken) is { } code)
        {
            store.RemoveEmailCode(code);
        }

        user.PendingEmail = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    public Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        store.GetRegistrationModeAsync(cancellationToken);

    /// <summary>A new account's user and actor, not yet staged.</summary>
    private static User NewUser(string handle, string displayName, string email, RequestOrigin origin, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7(now);
        return new User
        {
            Id = id,
            Actor = new Actor
            {
                Id = id,
                Kind = ActorKind.Human,
                Handle = handle,
                NormalizedHandle = AccountRules.NormalizeHandle(handle),
                DisplayName = displayName,
                CreatedAt = now,
            },
            Email = email,
            NormalizedEmail = AccountRules.NormalizeEmail(email),
            Locale = SupportedCultures.Find(origin.Culture) ?? SupportedCultures.Default,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Saves a new account with what <paramref name="stage"/> adds to it, using up the invite in invite-only mode. The
    /// very first account becomes admin: the check and the insert share one transaction under
    /// <see cref="AdvisoryLocks.Admins"/>, so two simultaneous first sign-ups cannot both win.
    /// </summary>
    private async Task<Result<AccountState>> CreateAccountAsync(
        User user,
        RegistrationMode mode,
        string? inviteCode,
        Func<CancellationToken, Task> stage,
        Func<Error> emailTaken,
        CancellationToken cancellationToken)
    {
        try
        {
            var registered = await unitOfWork.InTransactionAsync<AccountState>(
                AdvisoryLocks.Admins,
                async ct =>
                {
                    user.IsAdmin = !await store.AnyUsersAsync(ct);
                    store.AddUser(user);
                    await stage(ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    // After the insert, because the invite records who used it. The check before was only a
                    // courtesy; this conditional update is what stops two people using one code.
                    if (mode == RegistrationMode.InviteOnly
                        && !await store.TryUseInviteAsync(InviteCodes.Hash(inviteCode!), user.Id, user.CreatedAt, ct))
                    {
                        return InviteInvalid();
                    }

                    LogRegistered(logger, user.Id, user.IsAdmin);
                    return ToState(user);
                },
                cancellationToken);
            if (registered.IsSuccess)
            {
                // The admin user list and the overview's figures.
                await live.AdminChangedAsync(cancellationToken);
            }

            return registered;
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueHandle)
        {
            return UsernameTaken();
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueEmail)
        {
            return emailTaken();
        }
    }

    /// <summary>Why a username cannot be had, by its form alone; null when it may be.</summary>
    private string? UsernameError(string? username) =>
        !AccountRules.IsValidUsername(username) ? localizer["Validation.Username", AccountRules.UsernameMinLength, AccountRules.UsernameMaxLength].Value
        : AccountRules.IsReservedUsername(username!) ? localizer["Validation.UsernameReserved"].Value
        : null;

    private static AccountState ToState(User user) => new(
        user.Id,
        user.Actor.Handle!,
        user.Actor.DisplayName!,
        user.IsAdmin,
        user.EmailVerifiedAt is not null,
        user.Locale);

    /// <summary>The account an email address or username names, and the name as its counter knows it.</summary>
    private async Task<(User? User, string Normalized)> FindByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var byEmail = login.Contains('@', StringComparison.Ordinal);
        var normalized = byEmail ? AccountRules.NormalizeEmail(login) : AccountRules.NormalizeHandle(login);
        var user = byEmail
            ? await store.FindUserByEmailAsync(normalized, cancellationToken)
            : await store.FindUserByHandleAsync(normalized, cancellationToken);
        return (user, normalized);
    }

    /// <summary>
    /// Signing in to an account past its wrong passwords: Turnstile first, and only once it passed is the password
    /// checked and a code sent to the account's address, for the right password alone. The answer is the same either
    /// way, so a right guess cannot be told from a wrong one, and it gets nobody in without the mailbox. A banned
    /// account gets no code. The account stays so until its counter's window ends.
    /// </summary>
    private async Task<Result<LoginOutcome>> LoginPastFailuresAsync(
        User? user, string password, string? captchaToken, RequestOrigin origin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(captchaToken))
        {
            return Error.Validation(AccountErrorCodes.CaptchaRequired, localizer["Error.LoginCaptcha"]);
        }

        if (!await captcha.VerifyAsync(captchaToken, origin.ClientIp, cancellationToken))
        {
            return Error.Validation(AccountErrorCodes.CaptchaFailed, localizer["Error.CaptchaFailed"]);
        }

        // A stamp that names no code, which looks the same as one that does.
        var nowhere = LoginOutcome.CodeSent(DummyCodeStamp());
        if (user?.PasswordHash is null)
        {
            passwordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash(), password);
            return nowhere;
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed || user.BannedAt is not null)
        {
            return nowhere;
        }

        // A code sent in the last minute still stands; past the limits on codes none is sent, which is not said either.
        var last = await store.FindEmailCodeAsync(EmailCodePurpose.SignIn, user.NormalizedEmail, cancellationToken);
        if (TooSoon(last).IsSuccess && (await CheckCodeLimitsAsync(user, user.NormalizedEmail, origin, cancellationToken)).IsSuccess)
        {
            var issued = await IssueCodeAsync(EmailCodePurpose.SignIn, user, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return LoginOutcome.CodeSent(CodeStamp(issued));
        }

        return last is null ? nowhere : LoginOutcome.CodeSent(CodeStamp(last.CreatedAt));
    }

    /// <summary>
    /// Names one code sent for signing in or a reset: the time it was sent, which no two of an address's codes share (they are a
    /// minute apart at least). In milliseconds, which PostgreSQL keeps exactly (it keeps microseconds).
    /// </summary>
    private static string CodeStamp(DateTimeOffset createdAt) =>
        createdAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A stamp that names no code, shaped as a real one (13 digits): the encrypted TempData it is kept in must not be
    /// longer or shorter for an address with no account, or a wrong password.
    /// </summary>
    private static string DummyCodeStamp() => RandomNumberGenerator.GetString("0123456789", 13);

    private static string HashCode(EmailCodePurpose purpose, string normalizedEmail, string code) =>
        TextHashes.Sha256Hex($"{purpose}:{normalizedEmail}:{code}");

    private string TimingDummyHash() => timingDummyHash ??= passwordHasher.HashPassword(TimingDummy, "timing-dummy-password");

    /// <summary>Stages a fresh code (replacing any earlier one) and its email; the caller saves. Returns when it was sent.</summary>
    private Task<DateTimeOffset> IssueCodeAsync(EmailCodePurpose purpose, User user, CancellationToken cancellationToken) =>
        IssueCodeAsync(purpose, user, user.Email, cancellationToken);

    /// <summary>
    /// Stages a fresh code kept under the account's current address, sent to <paramref name="to"/>; the caller saves.
    /// Returns when it was sent.
    /// </summary>
    private async Task<DateTimeOffset> IssueCodeAsync(EmailCodePurpose purpose, User user, string to, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var now = time.GetUtcNow();
        var hash = HashCode(purpose, user.NormalizedEmail, code);
        var existing = await store.FindEmailCodeAsync(purpose, user.NormalizedEmail, cancellationToken);
        if (existing is null)
        {
            store.AddEmailCode(new EmailCode
            {
                Purpose = purpose,
                NormalizedEmail = user.NormalizedEmail,
                CodeHash = hash,
                ExpiresAt = now + CodeLifetime,
                CreatedAt = now,
            });
        }
        else
        {
            existing.CodeHash = hash;
            existing.Attempts = 0;
            existing.ExpiresAt = now + CodeLifetime;
            existing.CreatedAt = now;
        }

        outbox.Enqueue(
            AccountCodeEmail.MessageType,
            new AccountCodeEmail(to, user.Locale, purpose, codeProtector.Protect(code)),
            AccountsJsonContext.Default.AccountCodeEmail);
        return now;
    }

    /// <summary>Checks a code and stages its removal on success; every guess counts, right or wrong.</summary>
    private async Task<Result> ConsumeCodeAsync(EmailCodePurpose purpose, string normalizedEmail, string code, CancellationToken cancellationToken)
    {
        if (!await store.TryCountCodeAttemptAsync(purpose, normalizedEmail, time.GetUtcNow(), MaxCodeAttempts, cancellationToken))
        {
            return CodeInvalid();
        }

        var stored = await store.FindEmailCodeAsync(purpose, normalizedEmail, cancellationToken);
        if (stored is null || !TextHashes.FixedTimeEquals(stored.CodeHash, HashCode(purpose, normalizedEmail, code)))
        {
            return CodeInvalid();
        }

        store.RemoveEmailCode(stored);
        return Result.Success;
    }

    /// <summary>
    /// What both ways of moving to another address check, in order: the form, the password (its guesses are limited as
    /// <see cref="CheckPasswordAsync"/> says), and that the address is a new one nobody has.
    /// </summary>
    private async Task<Result<(User User, string Email)>> CheckNewEmailAsync(Guid userId, string? newEmail, string? currentPassword, CancellationToken cancellationToken)
    {
        if (!AccountRules.IsValidEmail(newEmail?.Trim()))
        {
            return InvalidInput(new() { [AccountFields.NewEmail] = [localizer["Validation.Email"]] });
        }

        var checkedPassword = await CheckPasswordAsync(userId, currentPassword, cancellationToken);
        if (!checkedPassword.IsSuccess)
        {
            return checkedPassword.Error;
        }

        var user = checkedPassword.Value;
        var email = AccountRules.NormalizeEmail(newEmail!);
        if (email == user.NormalizedEmail)
        {
            return InvalidInput(new() { [AccountFields.NewEmail] = [localizer["Validation.SameEmail"]] });
        }

        if (await store.EmailTakenAsync(email, cancellationToken))
        {
            return NewEmailTaken();
        }

        return (user, email);
    }

    /// <summary>
    /// The current password, asked again before a change. Its guesses have a counter of the account's own, refused past
    /// its limit: not the one signing in counts on (<see cref="RateLimitPolicies.LoginAccount"/>), which anyone can run
    /// up. An account that signs in only with Google or GitHub has none: the session is all there is to go by, as the
    /// settings say.
    /// </summary>
    private async Task<Result<User>> CheckPasswordAsync(Guid userId, string? currentPassword, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        if (user.PasswordHash is null)
        {
            var changes = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(user.Id), cancellationToken);
            return changes.IsSuccess ? user : changes.Error;
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            return InvalidInput(new() { [AccountFields.CurrentPassword] = [localizer["Validation.Required"]] });
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Login, RateLimitPartitions.Account(user.Id), cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed
            ? Error.Field(AccountErrorCodes.InvalidCredentials, AccountFields.CurrentPassword, localizer["Error.WrongPassword"])
            : user;
    }

    /// <summary>The limits on codes sent for an account once it exists: per IP, per address and per account.</summary>
    private Task<Result> CheckCodeLimitsAsync(User user, string normalizedEmail, RequestOrigin origin, CancellationToken cancellationToken) =>
        rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, RateLimitPartitions.Ip(origin.ClientIp)),
                new RateLimitTarget(RateLimitPolicies.EmailCode, RateLimitPartitions.Email(normalizedEmail)),
                new RateLimitTarget(RateLimitPolicies.EmailCodeAccount, RateLimitPartitions.Account(user.Id)),
            ],
            cancellationToken);

    /// <summary>Refuses another code while the last one is younger than <see cref="ResendInterval"/>.</summary>
    private Result TooSoon(EmailCode? last)
    {
        var wait = last is null ? TimeSpan.Zero : last.CreatedAt + ResendInterval - time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            return Result.Success;
        }

        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        return Error.RateLimited(wait, seconds == 1 ? localizer["Error.ResendTooSoon.One"] : localizer["Error.ResendTooSoon", seconds]);
    }

    private Error Banned() => Error.Forbidden(AccountErrorCodes.AccountBanned, localizer["Error.AccountBanned"]);

    private Error RegistrationClosed() => Error.Forbidden(AccountErrorCodes.RegistrationClosed, localizer["Error.RegistrationClosed"]);

    private Error AlreadyVerified() => Error.Conflict(AccountErrorCodes.AlreadyVerified, localizer["Error.AlreadyVerified"]);

    private Error NewEmailTaken() => Error.Field(AccountErrorCodes.EmailTaken, AccountFields.NewEmail, localizer["Error.EmailTaken"], ErrorKind.Conflict);

    private Error InvalidInput(Dictionary<string, string[]> fields) =>
        Error.Validation(AccountErrorCodes.InvalidInput, localizer["Error.InvalidInput"], fields);

    private Error UsernameTaken() => Error.Field(AccountErrorCodes.UsernameTaken, AccountFields.Username, localizer["Error.UsernameTaken"], ErrorKind.Conflict);

    private Error EmailTaken() => Error.Field(AccountErrorCodes.EmailTaken, AccountFields.Email, localizer["Error.EmailTaken"], ErrorKind.Conflict);

    private Error InviteInvalid() => Error.Field(AccountErrorCodes.InviteInvalid, AccountFields.InviteCode, localizer["Error.InviteInvalid"]);

    private Error CodeInvalid() => Error.Field(AccountErrorCodes.CodeInvalid, AccountFields.Code, localizer["Error.CodeInvalid"]);

    private Error InvalidCredentials() => Error.Unauthorized(AccountErrorCodes.InvalidCredentials, localizer["Error.InvalidCredentials"]);

    private Error AccountNotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.AccountNotFound"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} registered (admin: {IsAdmin})")]
    private static partial void LogRegistered(ILogger logger, Guid userId, bool isAdmin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} reset its password; all sessions ended")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} changed its password; its other sessions ended")]
    private static partial void LogPasswordChanged(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} moved to another email address")]
    private static partial void LogEmailChanged(ILogger logger, Guid userId);
}
