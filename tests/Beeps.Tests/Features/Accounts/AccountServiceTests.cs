using Beeps.Features.Accounts;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Accounts;

public sealed class AccountServiceTests
{
    private readonly IAccountStore store = Substitute.For<IAccountStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ICaptchaVerifier captcha = Substitute.For<ICaptchaVerifier>();
    private readonly IPasswordHasher<User> hasher = Substitute.For<IPasswordHasher<User>>();
    private readonly IRateLimitStore rateLimitStore = Substitute.For<IRateLimitStore>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();
    private readonly List<AccountCodeEmail> sentCodes = [];

    public AccountServiceTests()
    {
        captcha.VerifyAsync(default, default!, TestSupport.Ct).ReturnsForAnyArgs(true);
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        store.GetRegistrationModeAsync(default).ReturnsForAnyArgs(RegistrationMode.Open);
        store.TryCountCodeAttemptAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        hasher.HashPassword(default!, default!).ReturnsForAnyArgs(ci => $"hashed:{ci.ArgAt<string>(1)}");
        hasher.VerifyHashedPassword(default!, default!, default!).ReturnsForAnyArgs(ci =>
            ci.ArgAt<string>(1) == $"hashed:{ci.ArgAt<string>(2)}" ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed);
        outbox.When(o => o.Enqueue(AccountCodeEmail.MessageType, Arg.Any<AccountCodeEmail>(), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<AccountCodeEmail>>()))
            .Do(ci => sentCodes.Add(ci.ArgAt<AccountCodeEmail>(1)));
    }

    // ---- Register ----------------------------------------------------------

    [Fact]
    public async Task RegisterRejectsBadInputBeforeAskingTurnstile()
    {
        var result = await CreateService().RegisterAsync(new RegisterRequest("not-an-email", "a.b", "short", null, "token"), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidInput, result.Error?.Code);
        Assert.Equal([AccountFields.Email, AccountFields.Password, AccountFields.Username], result.Error!.Fields!.Keys.Order());
        await captcha.DidNotReceiveWithAnyArgs().VerifyAsync(default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task RegisterRejectsReservedUsernames()
    {
        var result = await CreateService().RegisterAsync(Request(username: "Admin"), TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Validation.UsernameReserved"], result.Error?.Fields?[AccountFields.Username]!);
    }

    [Fact]
    public async Task RegisterStopsWhenTurnstileFails()
    {
        captcha.VerifyAsync("token", "203.0.113.7", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CaptchaFailed, result.Error?.Code);
        store.DidNotReceiveWithAnyArgs().AddUser(default!);
        await rateLimitStore.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task FirstUserBecomesAdminUnderTheAdminLock()
    {
        store.AnyUsersAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin with { Culture = "zh-Hans" }, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAdmin);
        Assert.False(result.Value.EmailVerified);
        Assert.Equal([AdvisoryLocks.Admins], unitOfWork.Locks);
        store.Received(1).AddUser(Arg.Is<User>(u =>
            u.IsAdmin && u.Actor.Handle == "Tom_1" && u.Actor.NormalizedHandle == "tom_1" && u.Actor.Kind == ActorKind.Human
            && u.NormalizedEmail == "tom@example.com" && u.PasswordHash == "hashed:correct horse" && u.Locale == "zh-Hans"
            && u.Id == u.Actor.Id));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task LaterUsersAreNotAdmins()
    {
        store.AnyUsersAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.False(result.Value.IsAdmin);
        store.Received(1).AddUser(Arg.Is<User>(u => !u.IsAdmin));
    }

    [Fact]
    public async Task RegisterSendsAVerificationCodeThatOnlyTheEmailCarries()
    {
        EmailCode? stored = null;
        store.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());

        await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        var email = Assert.Single(sentCodes);
        Assert.Equal(EmailCodePurpose.VerifyEmail, email.Purpose);
        Assert.Equal("Tom@Example.com", email.To);
        var code = dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose).Unprotect(email.ProtectedCode);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.NotNull(stored);
        Assert.DoesNotContain(code, stored.CodeHash, StringComparison.Ordinal);
        Assert.Equal(TestData.Now + AccountService.CodeLifetime, stored.ExpiresAt);
    }

    [Fact]
    public async Task RegisterIsRateLimitedPerIp()
    {
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(5)));

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        store.DidNotReceiveWithAnyArgs().AddUser(default!);
    }

    [Fact]
    public async Task ClosedRegistrationIsRefused()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.Closed);

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.RegistrationClosed, result.Error?.Code);
    }

    [Fact]
    public async Task InviteOnlyNeedsACode()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Validation.InviteRequired"], result.Error?.Fields?[AccountFields.InviteCode]!);
    }

    [Fact]
    public async Task AWrongInviteDoesNotUseUpTheEmailCodeQuota()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);
        store.IsInviteUsableAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(false);

        var result = await CreateService().RegisterAsync(Request(invite: "AAAA-BBBB"), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InviteInvalid, result.Error?.Code);
        await rateLimitStore.Received(1).TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.All(x => x.Key.StartsWith("Register:", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
        store.DidNotReceiveWithAnyArgs().AddUser(default!);
    }

    [Fact]
    public async Task AValidInviteIsConsumedForTheNewUser()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);
        store.IsInviteUsableAsync(InviteCodes.Hash("ab12-cd34"), TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        store.TryUseInviteAsync(InviteCodes.Hash("AB12CD34"), Arg.Any<Guid>(), TestData.Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RegisterAsync(Request(invite: "ab12-cd34"), TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.Received(1).TryUseInviteAsync(Arg.Any<string>(), result.Value.UserId, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInviteTakenMeanwhileRollsTheRegistrationBack()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);
        store.IsInviteUsableAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(true);
        store.TryUseInviteAsync(default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(false);

        var result = await CreateService().RegisterAsync(Request(invite: "AB12CD34"), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InviteInvalid, result.Error?.Code);
        Assert.Equal(1, unitOfWork.Rollbacks);
    }

    [Fact]
    public async Task TakenUsernameAndEmailAreReportedOnTheirFields()
    {
        store.HandleTakenAsync("tom_1", Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var username = await service.RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);
        store.HandleTakenAsync("tom_1", Arg.Any<CancellationToken>()).Returns(false);
        store.EmailTakenAsync("tom@example.com", Arg.Any<CancellationToken>()).Returns(true);
        var email = await service.RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.UsernameTaken, username.Error?.Code);
        Assert.Equal(ErrorKind.Conflict, username.Error?.Kind);
        Assert.True(username.Error?.Fields?.ContainsKey(AccountFields.Username));
        Assert.Equal(AccountErrorCodes.EmailTaken, email.Error?.Code);
    }

    [Fact]
    public async Task LosingTheUniqueIndexRaceIsStillUsernameTaken()
    {
        unitOfWork.FailNextSave = UniqueViolationException.ForIndex(AccountIndexes.UniqueHandle, new InvalidOperationException());

        var result = await CreateService().RegisterAsync(Request(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.UsernameTaken, result.Error?.Code);
    }

    // ---- Verify / resend -----------------------------------------------------

    [Fact]
    public async Task TheRightCodeVerifiesTheEmail()
    {
        var user = TestData.User(verified: false);
        var (code, stored) = await IssueVerificationCodeAsync(user);

        var result = await CreateService().VerifyEmailAsync(user.Id, code, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.Now, user.EmailVerifiedAt);
        store.Received(1).RemoveEmailCode(stored);
    }

    [Fact]
    public async Task AWrongCodeCountsAsAnAttempt()
    {
        var user = TestData.User(verified: false);
        await IssueVerificationCodeAsync(user);

        var result = await CreateService().VerifyEmailAsync(user.Id, "000000", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        Assert.Null(user.EmailVerifiedAt);
        await store.Received(1).TryCountCodeAttemptAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, TestData.Now, AccountService.MaxCodeAttempts, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoAttemptsLeftMeansEvenTheRightCodeFails()
    {
        var user = TestData.User(verified: false);
        var (code, _) = await IssueVerificationCodeAsync(user);
        store.TryCountCodeAttemptAsync(default, default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(false);

        var result = await CreateService().VerifyEmailAsync(user.Id, code, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        Assert.Null(user.EmailVerifiedAt);
    }

    [Fact]
    public async Task MalformedCodesAreRejectedWithoutALookup()
    {
        var result = await CreateService().VerifyEmailAsync(Guid.NewGuid(), "12a456", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task ResendReplacesTheCodeAndIsRateLimited()
    {
        var user = TestData.User(verified: false);
        var existing = new EmailCode { Purpose = EmailCodePurpose.VerifyEmail, NormalizedEmail = user.NormalizedEmail, CodeHash = "old", Attempts = 4 };
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateService().ResendVerificationAsync(user.Id, TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.NotEqual("old", existing.CodeHash);
        Assert.Equal(0, existing.Attempts);
        Assert.Single(sentCodes);
        // The address has the strict limit; the IP a looser one of its own, so people sharing an IP are not blocked.
        await rateLimitStore.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.Count == 2
                && c[0].Key.StartsWith(RateLimitPolicies.EmailCodeIp + ":", StringComparison.Ordinal)
                && c[1].Key.StartsWith(RateLimitPolicies.EmailCode + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendForAVerifiedAccountIsAConflict()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ResendVerificationAsync(user.Id, TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AlreadyVerified, result.Error?.Code);
        Assert.Empty(sentCodes);
    }

    // ---- Login ---------------------------------------------------------------

    [Theory]
    [InlineData("tom@example.com")]
    [InlineData("TOM")]
    public async Task LoginWorksWithEmailOrUsername(string login)
    {
        var user = TestData.User(passwordHash: "hashed:correct horse");
        store.FindUserByEmailAsync("tom@example.com", Arg.Any<CancellationToken>()).Returns(user);
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().LoginAsync(login, "correct horse", TestData.Origin, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal("stamp-1", result.Value.SecurityStamp);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownUserLookTheSame()
    {
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(TestData.User(passwordHash: "hashed:correct horse"));
        var service = CreateService();

        var wrongPassword = await service.LoginAsync("tom", "nope", TestData.Origin, TestSupport.Ct);
        var unknown = await service.LoginAsync("nobody", "nope", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidCredentials, wrongPassword.Error?.Code);
        Assert.Equal(wrongPassword.Error, unknown.Error);
        // The unknown account still paid for a hash check.
        hasher.ReceivedWithAnyArgs(2).VerifyHashedPassword(default!, default!, default!);
    }

    [Fact]
    public async Task LoginIsLimitedByIpAndByAccount()
    {
        await CreateService().LoginAsync("Tom", "x", TestData.Origin, TestSupport.Ct);

        await rateLimitStore.Received(1).TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.Count == 2 && c[0].Key != c[1].Key), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BannedAccountsCannotLogIn()
    {
        var user = TestData.User(passwordHash: "hashed:correct horse");
        user.BannedAt = TestData.Now;
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().LoginAsync("tom", "correct horse", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AccountBanned, result.Error?.Code);
    }

    [Fact]
    public async Task OutdatedHashesAreUpgradedOnLogin()
    {
        var user = TestData.User(passwordHash: "old-format");
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(user);
        hasher.VerifyHashedPassword(user, "old-format", "correct horse").Returns(PasswordVerificationResult.SuccessRehashNeeded);

        var result = await CreateService().LoginAsync("tom", "correct horse", TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:correct horse", user.PasswordHash);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task LoginNeedsBothFields()
    {
        var result = await CreateService().LoginAsync(" ", "", TestData.Origin, TestSupport.Ct);

        Assert.Equal([AccountFields.Login, AccountFields.Password], result.Error?.Fields?.Keys.Order());
    }

    // ---- Password reset -------------------------------------------------------

    [Fact]
    public async Task ResetRequestForAnUnknownAddressSucceedsQuietly()
    {
        var result = await CreateService().RequestPasswordResetAsync("nobody@example.com", "token", TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(sentCodes);
        await rateLimitStore.ReceivedWithAnyArgs(1).TryAcquireAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task ResetRequestSendsAResetCode()
    {
        var user = TestData.User();
        store.FindUserByEmailAsync("tom@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().RequestPasswordResetAsync(" Tom@Example.com ", "token", TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailCodePurpose.ResetPassword, Assert.Single(sentCodes).Purpose);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task ResetRequestNeedsTurnstile()
    {
        captcha.VerifyAsync(default, default!, TestSupport.Ct).ReturnsForAnyArgs(false);

        var result = await CreateService().RequestPasswordResetAsync("tom@example.com", null, TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CaptchaFailed, result.Error?.Code);
    }

    [Fact]
    public async Task ResetSetsThePasswordAndEndsEverySession()
    {
        var user = TestData.User(verified: false);
        var code = await IssueResetCodeAsync(user);

        var result = await CreateService().ResetPasswordAsync(new PasswordResetRequest(user.Email, code, "new password!"), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:new password!", user.PasswordHash);
        Assert.NotEqual("stamp-1", user.SecurityStamp);
        Assert.Equal(TestData.Now, user.EmailVerifiedAt);
    }

    [Fact]
    public async Task ResetForAnUnknownAddressIsJustAWrongCode()
    {
        var result = await CreateService().ResetPasswordAsync(new PasswordResetRequest("nobody@example.com", "123456", "new password!"), TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task ResetValidatesItsInput()
    {
        var result = await CreateService().ResetPasswordAsync(new PasswordResetRequest("x", "12", "short"), TestSupport.Ct);

        Assert.Equal([AccountFields.Code, AccountFields.Email, AccountFields.Password], result.Error?.Fields?.Keys.Order());
    }

    // ---- Settings and sessions ----------------------------------------------

    [Fact]
    public async Task LocaleIsSavedOnlyForSupportedCultures()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService();

        var supported = await service.SetLocaleAsync(user.Id, "ZH-HANS", TestSupport.Ct);
        var unsupported = await service.SetLocaleAsync(user.Id, "fr", TestSupport.Ct);

        Assert.True(supported.IsSuccess);
        Assert.Equal("zh-Hans", user.Locale);
        Assert.False(unsupported.IsSuccess);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task SessionsAndRegistrationModeComeFromTheStore()
    {
        var session = new AccountSession(Guid.NewGuid(), "tom", "Tom", false, true, "en", "stamp");
        store.GetSessionAsync(session.UserId, Arg.Any<CancellationToken>()).Returns(session);
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);
        var service = CreateService();

        Assert.Same(session, await service.GetSessionAsync(session.UserId, TestSupport.Ct));
        Assert.Null(await service.GetSessionAsync(Guid.NewGuid(), TestSupport.Ct));
        Assert.Equal(RegistrationMode.InviteOnly, await service.GetRegistrationModeAsync(TestSupport.Ct));
    }

    private static RegisterRequest Request(string username = "Tom_1", string? invite = null) =>
        new("Tom@Example.com", username, "correct horse", invite, "token");

    /// <summary>Issues a real code through the service (resend), so tests verify against the actual hash.</summary>
    private async Task<(string Code, EmailCode Stored)> IssueVerificationCodeAsync(User user)
    {
        EmailCode? stored = null;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());
        await CreateService().ResendVerificationAsync(user.Id, TestData.Origin, TestSupport.Ct);
        store.FindEmailCodeAsync(EmailCodePurpose.VerifyEmail, user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(stored);
        return (Unprotect(sentCodes[^1]), stored!);
    }

    private async Task<string> IssueResetCodeAsync(User user)
    {
        EmailCode? stored = null;
        store.FindUserByEmailAsync(user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(user);
        store.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());
        await CreateService().RequestPasswordResetAsync(user.Email, "token", TestData.Origin, TestSupport.Ct);
        store.FindEmailCodeAsync(EmailCodePurpose.ResetPassword, user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(stored);
        return Unprotect(sentCodes[^1]);
    }

    private string Unprotect(AccountCodeEmail email) =>
        dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose).Unprotect(email.ProtectedCode);

    private AccountService CreateService()
    {
        var limits = Substitute.For<IOptionsMonitor<RateLimitOptions>>();
        limits.CurrentValue.Returns(new RateLimitOptions
        {
            Policies = RateLimitPolicies.All.ToDictionary(p => p, _ => new RateLimitPolicyOptions { Rules = [new RateLimitRuleOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }] }),
        });
        var localizer = new TestLocalizer();
        return new AccountService(
            store,
            unitOfWork,
            outbox,
            captcha,
            hasher,
            new RateLimitService(rateLimitStore, limits, localizer, NullLogger<RateLimitService>.Instance),
            dataProtection,
            TestData.Time(),
            localizer,
            NullLogger<AccountService>.Instance);
    }
}
