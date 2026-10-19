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

/// <summary>Human accounts: registration, email codes, sign-in, password reset and email changes (PLAN 3.1, 6.13–6.15).</summary>
public sealed partial class AccountService(
    IAccountStore store,
    ISessionStore sessions,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    ICaptchaVerifier captcha,
    IPasswordHasher<User> passwordHasher,
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

        if (!AccountRules.IsValidUsername(request.Username))
        {
            fields[AccountFields.Username] = [localizer["Validation.Username", AccountRules.UsernameMinLength, AccountRules.UsernameMaxLength]];
        }
        else if (AccountRules.IsReservedUsername(request.Username!))
        {
            fields[AccountFields.Username] = [localizer["Validation.UsernameReserved"]];
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
            return Error.Forbidden(AccountErrorCodes.RegistrationClosed, localizer["Error.RegistrationClosed"]);
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

        var id = Guid.CreateVersion7(now);
        var user = new User
        {
            Id = id,
            Actor = new Actor
            {
                Id = id,
                Kind = ActorKind.Human,
                Handle = handle,
                NormalizedHandle = normalizedHandle,
                DisplayName = handle,
                CreatedAt = now,
            },
            Email = request.Email!.Trim(),
            NormalizedEmail = email,
            PasswordHash = "",
            Locale = SupportedCultures.Find(origin.Culture) ?? SupportedCultures.Default,
            CreatedAt = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);

        try
        {
            var registered = await unitOfWork.InTransactionAsync<AccountState>(
                AdvisoryLocks.Admins,
                async ct =>
                {
                    user.IsAdmin = !await store.AnyUsersAsync(ct);
                    store.AddUser(user);
                    await IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    // After the insert, because the invite records who used it. The check above was only a
                    // courtesy; this conditional update is what stops two people using one code.
                    if (mode == RegistrationMode.InviteOnly
                        && !await store.TryUseInviteAsync(InviteCodes.Hash(request.InviteCode!), id, now, ct))
                    {
                        return InviteInvalid();
                    }

                    LogRegistered(logger, id, user.IsAdmin);
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
            return EmailTaken();
        }
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

        var consumed = await ConsumeCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        user.EmailVerifiedAt = time.GetUtcNow();
        // From now on the person can be found by search.
        outbox.SyncAccountToSearch(user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

        // The code sent to the old address stops working.
        if (await store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, cancellationToken) is { } old)
        {
            store.RemoveEmailCode(old);
        }

        user.Email = newEmail!.Trim();
        user.NormalizedEmail = email;
        await IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueEmail)
        {
            return NewEmailTaken();
        }

        // The admin user list shows the address.
        await live.AdminChangedAsync(cancellationToken);
        LogEmailChanged(logger, user.Id);
        return Result.Success;
    }

    /// <param name="login">Email address or username.</param>
    public async Task<Result<AccountState>> LoginAsync(string? login, string? password, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
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

        var byEmail = login.Contains('@', StringComparison.Ordinal);
        var normalized = byEmail ? AccountRules.NormalizeEmail(login) : AccountRules.NormalizeHandle(login);
        var user = byEmail
            ? await store.FindUserByEmailAsync(normalized, cancellationToken)
            : await store.FindUserByHandleAsync(normalized, cancellationToken);
        // An account has one counter, by address or username alike. A name no account has gets its own, so guesses
        // at it are refused just as soon and the limit does not tell which names exist.
        var limited = await rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.Login, RateLimitPartitions.Ip(origin.ClientIp)),
                new RateLimitTarget(RateLimitPolicies.Login, user is null ? RateLimitPartitions.Login(normalized) : RateLimitPartitions.Account(user.Id)),
            ],
            cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        if (user is null)
        {
            // Same work as a wrong password, so response times do not reveal which accounts exist.
            passwordHasher.VerifyHashedPassword(TimingDummy, TimingDummyHash(), password);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        if (user.BannedAt is not null)
        {
            return Error.Forbidden(AccountErrorCodes.AccountBanned, localizer["Error.AccountBanned"]);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToState(user);
    }

    /// <summary>
    /// Sends a reset code if the address belongs to an account. The answer is the same either way, so
    /// the form cannot be used to find out who has an account.
    /// </summary>
    public async Task<Result> RequestPasswordResetAsync(string? email, string? captchaToken, RequestOrigin origin, CancellationToken cancellationToken)
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
                new RateLimitTarget(RateLimitPolicies.EmailCode, RateLimitPartitions.Email(normalized)),
            ],
            cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        var user = await store.FindUserByEmailAsync(normalized, cancellationToken);
        if (user is null || user.BannedAt is not null)
        {
            return Result.Success;
        }

        await IssueCodeAsync(EmailCodePurpose.ResetPassword, user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success;
    }

    /// <summary>Sets the new password and ends every existing session.</summary>
    public async Task<Result> ResetPasswordAsync(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        // Outside the transaction: a wrong guess must stay counted even though nothing else is saved.
        var consumed = await ConsumeCodeAsync(EmailCodePurpose.ResetPassword, user.NormalizedEmail, request.Code!, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        // The new password and the end of every old session commit together.
        var verifies = user.EmailVerifiedAt is null;
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
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

        await sessions.ForgetAllAsync(user.Id, cancellationToken);
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
            user.Email,
            waiting,
            user.Locale,
            new ProfilePrivacy(user.HideLikes, user.HideFollows, user.HideFollowCounts));
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

    /// <summary>The human's own display name and bio (PLAN 2.2); the username never changes.</summary>
    public async Task<Result> UpdateProfileAsync(Guid userId, string? displayName, string? bio, CancellationToken cancellationToken)
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
        outbox.SyncAccountToSearch(user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
        await live.ActorChangedAsync(user.Id, cancellationToken);
        return Result.Success;
    }

    /// <summary>
    /// Needs the current password. Every other session ends; the one the change was made in stays, since it
    /// just proved it knows the password. Guesses count against the same limit as signing in to the account.
    /// </summary>
    /// <param name="currentSessionId">The session making the change; null ends every session.</param>
    public async Task<Result> ChangePasswordAsync(Guid userId, Guid? currentSessionId, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(currentPassword))
        {
            fields[AccountFields.CurrentPassword] = [localizer["Validation.Required"]];
        }

        if (!AccountRules.IsValidPassword(newPassword))
        {
            fields[AccountFields.NewPassword] = [localizer["Validation.Password", AccountRules.PasswordMinLength, AccountRules.PasswordMaxLength]];
        }

        if (fields.Count > 0)
        {
            return InvalidInput(fields);
        }

        var checkedPassword = await CheckPasswordAsync(userId, currentPassword!, cancellationToken);
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
        await sessions.ForgetAllAsync(user.Id, cancellationToken);
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

    private static AccountState ToState(User user) => new(
        user.Id,
        user.Actor.Handle!,
        user.Actor.DisplayName!,
        user.IsAdmin,
        user.EmailVerifiedAt is not null,
        user.Locale);

    private static string HashCode(EmailCodePurpose purpose, string normalizedEmail, string code) =>
        TextHashes.Sha256Hex($"{purpose}:{normalizedEmail}:{code}");

    private string TimingDummyHash() => timingDummyHash ??= passwordHasher.HashPassword(TimingDummy, "timing-dummy-password");

    /// <summary>Stages a fresh code (replacing any earlier one) and its email; the caller saves.</summary>
    private Task IssueCodeAsync(EmailCodePurpose purpose, User user, CancellationToken cancellationToken) =>
        IssueCodeAsync(purpose, user, user.Email, cancellationToken);

    /// <summary>
    /// Stages a fresh code kept under the account's current address, sent to <paramref name="to"/>; the caller saves.
    /// </summary>
    private async Task IssueCodeAsync(EmailCodePurpose purpose, User user, string to, CancellationToken cancellationToken)
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
    /// What both ways of moving to another address check, in order: the form, the password (its guesses count as
    /// signing in to the account), and that the address is a new one nobody has.
    /// </summary>
    private async Task<Result<(User User, string Email)>> CheckNewEmailAsync(Guid userId, string? newEmail, string? currentPassword, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string[]>();
        if (!AccountRules.IsValidEmail(newEmail?.Trim()))
        {
            fields[AccountFields.NewEmail] = [localizer["Validation.Email"]];
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            fields[AccountFields.CurrentPassword] = [localizer["Validation.Required"]];
        }

        if (fields.Count > 0)
        {
            return InvalidInput(fields);
        }

        var checkedPassword = await CheckPasswordAsync(userId, currentPassword!, cancellationToken);
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

    /// <summary>The current password, asked again before a change; its guesses count as signing in to the account.</summary>
    private async Task<Result<User>> CheckPasswordAsync(Guid userId, string currentPassword, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
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
