using System.Globalization;
using System.Security.Cryptography;
using Beeps.Features.Localization;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>Human accounts: registration, email codes, sign-in and password reset (PLAN 3.1, 6.13–6.15).</summary>
public sealed partial class AccountService(
    IAccountStore store,
    ISessionStore sessions,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    ICaptchaVerifier captcha,
    IPasswordHasher<User> passwordHasher,
    RateLimitService rateLimits,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<AccountService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public const int MaxCodeAttempts = 5;

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
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Register, IpPartition(origin), cancellationToken);
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
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, IpPartition(origin)),
                new RateLimitTarget(RateLimitPolicies.EmailCode, EmailPartition(email)),
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
            return await unitOfWork.InTransactionAsync<AccountState>(
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
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
            return Error.Conflict(AccountErrorCodes.AlreadyVerified, localizer["Error.AlreadyVerified"]);
        }

        var limited = await rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, IpPartition(origin)),
                new RateLimitTarget(RateLimitPolicies.EmailCode, EmailPartition(user.NormalizedEmail)),
            ],
            cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        await IssueCodeAsync(EmailCodePurpose.VerifyEmail, user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
        var limited = await rateLimits.CheckAsync(
            [
                new RateLimitTarget(RateLimitPolicies.Login, IpPartition(origin)),
                new RateLimitTarget(RateLimitPolicies.Login, $"login:{normalized}"),
            ],
            cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited.Error;
        }

        var user = byEmail
            ? await store.FindUserByEmailAsync(normalized, cancellationToken)
            : await store.FindUserByHandleAsync(normalized, cancellationToken);
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
                new RateLimitTarget(RateLimitPolicies.PasswordReset, IpPartition(origin)),
                new RateLimitTarget(RateLimitPolicies.PasswordReset, EmailPartition(normalized)),
                new RateLimitTarget(RateLimitPolicies.EmailCodeIp, IpPartition(origin)),
                new RateLimitTarget(RateLimitPolicies.EmailCode, EmailPartition(normalized)),
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
        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
                // The code proved the address works.
                user.EmailVerifiedAt ??= now;
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
        return Result.Success;
    }

    /// <summary>What the settings page shows and edits.</summary>
    public async Task<Result<AccountSettings>> GetSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await store.FindUserAsync(userId, cancellationToken);
        return user is null
            ? AccountNotFound()
            : new AccountSettings(user.Actor.Handle!, user.Actor.DisplayName!, user.Actor.Bio, user.Email, user.Locale);
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

        user.Actor.DisplayName = name;
        user.Actor.Bio = cleanBio;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetAccountAsync(user.Id, cancellationToken);
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

        var user = await store.FindUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return AccountNotFound();
        }

        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Login, $"login:{user.Actor.NormalizedHandle}", cancellationToken);
        if (!limited.IsSuccess)
        {
            return limited;
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword!) == PasswordVerificationResult.Failed)
        {
            return FieldError(ErrorKind.Validation, AccountErrorCodes.InvalidCredentials, "Error.WrongPassword", AccountFields.CurrentPassword);
        }

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
        LogPasswordChanged(logger, user.Id);
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

    private static string IpPartition(RequestOrigin origin) => $"ip:{origin.ClientIp}";

    private static string EmailPartition(string normalizedEmail) => $"email:{normalizedEmail}";

    private static string HashCode(EmailCodePurpose purpose, string normalizedEmail, string code) =>
        TextHashes.Sha256Hex($"{purpose}:{normalizedEmail}:{code}");

    private string TimingDummyHash() => timingDummyHash ??= passwordHasher.HashPassword(TimingDummy, "timing-dummy-password");

    /// <summary>Stages a fresh code (replacing any earlier one) and its email; the caller saves.</summary>
    private async Task IssueCodeAsync(EmailCodePurpose purpose, User user, CancellationToken cancellationToken)
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
            new AccountCodeEmail(user.Email, user.Locale, purpose, codeProtector.Protect(code)),
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

    private Error InvalidInput(Dictionary<string, string[]> fields) =>
        Error.Validation(AccountErrorCodes.InvalidInput, localizer["Error.InvalidInput"], fields);

    private Error UsernameTaken() => FieldError(ErrorKind.Conflict, AccountErrorCodes.UsernameTaken, "Error.UsernameTaken", AccountFields.Username);

    private Error EmailTaken() => FieldError(ErrorKind.Conflict, AccountErrorCodes.EmailTaken, "Error.EmailTaken", AccountFields.Email);

    private Error InviteInvalid() => FieldError(ErrorKind.Validation, AccountErrorCodes.InviteInvalid, "Error.InviteInvalid", AccountFields.InviteCode);

    private Error CodeInvalid() => FieldError(ErrorKind.Validation, AccountErrorCodes.CodeInvalid, "Error.CodeInvalid", AccountFields.Code);

    private Error InvalidCredentials() => Error.Unauthorized(AccountErrorCodes.InvalidCredentials, localizer["Error.InvalidCredentials"]);

    private Error AccountNotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.AccountNotFound"]);

    private Error FieldError(ErrorKind kind, string code, string resource, string field)
    {
        string message = localizer[resource];
        return new Error(kind, code, message) { Fields = new Dictionary<string, string[]> { [field] = [message] } };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} registered (admin: {IsAdmin})")]
    private static partial void LogRegistered(ILogger logger, Guid userId, bool isAdmin);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} reset its password; all sessions ended")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} changed its password; its other sessions ended")]
    private static partial void LogPasswordChanged(ILogger logger, Guid userId);
}
