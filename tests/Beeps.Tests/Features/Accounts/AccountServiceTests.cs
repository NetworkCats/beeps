using Beeps.Features.Accounts;
using Beeps.Features.Live;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Accounts;

public sealed partial class AccountServiceTests
{
    private readonly IAccountStore store = Substitute.For<IAccountStore>();
    private readonly ISessionStore sessions = Substitute.For<ISessionStore>();
    private readonly ImmediateUnitOfWork unitOfWork = new();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ILiveEventStore liveEvents = Substitute.For<ILiveEventStore>();
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
        // Only verified people can be found by search.
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(user.Id, false), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
    }

    [Fact]
    public async Task ACodeForAnAddressMovedAwayMeanwhileVerifiesNothing()
    {
        var user = TestData.User(verified: false);
        var (code, _) = await IssueVerificationCodeAsync(user);
        // A move to another address, sent alongside, committed first.
        store.LeftUnverifiedEmailAsync(user.Id, user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().VerifyEmailAsync(user.Id, code, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        Assert.Null(user.EmailVerifiedAt);
        Assert.Equal([AdvisoryLocks.UnverifiedEmail(user.Id)], unitOfWork.Locks);
        Assert.Equal(1, unitOfWork.Rollbacks);
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
        // The address has the strict limit; the IP a looser one of its own, so people sharing an IP are not blocked;
        // the account one too, whatever addresses it moves between.
        await rateLimitStore.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.Count == 3
                && c[0].Key.StartsWith(RateLimitPolicies.EmailCodeIp + ":", StringComparison.Ordinal)
                && c[1].Key.StartsWith(RateLimitPolicies.EmailCode + ":", StringComparison.Ordinal)
                && c[2].Key.StartsWith(RateLimitPolicies.EmailCodeAccount + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewCodeWaitsAMinuteAfterTheLastOne()
    {
        var user = TestData.User(verified: false);
        var (_, stored) = await IssueVerificationCodeAsync(user);
        sentCodes.Clear();
        stored.CreatedAt = TestData.Now - TimeSpan.FromSeconds(20);

        var result = await CreateService().ResendVerificationAsync(user.Id, TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        Assert.Equal(TimeSpan.FromSeconds(40), result.Error?.RetryAfter);
        Assert.Equal("Error.ResendTooSoon(40)", result.Error?.Detail);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task OneSecondLeftIsSaidInTheSingular()
    {
        var user = TestData.User(verified: false);
        var (_, stored) = await IssueVerificationCodeAsync(user);
        stored.CreatedAt = TestData.Now - AccountService.ResendInterval + TimeSpan.FromMilliseconds(500);

        var result = await CreateService().ResendVerificationAsync(user.Id, TestData.Origin, TestSupport.Ct);

        Assert.Equal("Error.ResendTooSoon.One", result.Error?.Detail);
    }

    [Fact]
    public async Task TheVerificationPageKnowsWhenAnotherCodeMayBeSent()
    {
        var user = TestData.User(verified: false);
        await IssueVerificationCodeAsync(user);

        var verification = await CreateService().GetVerificationAsync(user.Id, TestSupport.Ct);

        Assert.Equal(new Verification("tom@example.com", TestData.Now + AccountService.ResendInterval), verification.Value);
    }

    [Fact]
    public async Task AMistypedAddressIsReplacedAndTheCodeGoesToTheNewOne()
    {
        var user = TestData.User(verified: false, passwordHash: "hashed:old password");
        var (_, old) = await IssueVerificationCodeAsync(user);
        sentCodes.Clear();

        var result = await CreateService().ChangeUnverifiedEmailAsync(user.Id, " Tom@Example.org ", "old password", TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Tom@Example.org", "tom@example.org", null), (user.Email, user.NormalizedEmail, user.EmailVerifiedAt));
        store.Received(1).RemoveEmailCode(old);
        var email = Assert.Single(sentCodes);
        Assert.Equal((EmailCodePurpose.VerifyEmail, "Tom@Example.org"), (email.Purpose, email.To));
        store.Received().AddEmailCode(Arg.Is<EmailCode>(c => c.Purpose == EmailCodePurpose.VerifyEmail && c.NormalizedEmail == "tom@example.org"));
        Assert.Single(TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged));
    }

    [Fact]
    public async Task AnAddressVerifiedMeanwhileIsNotMovedAway()
    {
        var user = TestData.User(verified: false, passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        // Its code, sent alongside, committed first.
        store.LeftUnverifiedEmailAsync(user.Id, "tom@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().ChangeUnverifiedEmailAsync(user.Id, "tom@example.org", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AlreadyVerified, result.Error?.Code);
        Assert.Equal("tom@example.com", user.NormalizedEmail);
        Assert.Equal([AdvisoryLocks.UnverifiedEmail(user.Id)], unitOfWork.Locks);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task AnotherAddressBeforeVerifyingNeedsThePassword()
    {
        var user = TestData.User(verified: false, passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ChangeUnverifiedEmailAsync(user.Id, "tom@example.org", "guess", TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Error.WrongPassword"], result.Error?.Fields?[AccountFields.CurrentPassword]!);
        Assert.Equal("tom@example.com", user.NormalizedEmail);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task AVerifiedAccountChangesItsAddressFromTheSettings()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ChangeUnverifiedEmailAsync(user.Id, "tom@example.org", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AlreadyVerified, result.Error?.Code);
        Assert.Equal("tom@example.com", user.NormalizedEmail);
    }

    [Fact]
    public async Task AnAddressWithAnAccountCannotBeTakenBeforeVerifyingEither()
    {
        var user = TestData.User(verified: false, passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.EmailTakenAsync("alice@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().ChangeUnverifiedEmailAsync(user.Id, "alice@example.com", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Error.EmailTaken"], result.Error?.Fields?[AccountFields.NewEmail]!);
        Assert.Empty(sentCodes);
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
        Assert.Equal("tom", result.Value.Handle);
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
    public async Task SigningInByEmailOrUsernameCountsAgainstOneLimit()
    {
        var user = TestData.User(passwordHash: "hashed:correct horse");
        store.FindUserByEmailAsync("tom@example.com", Arg.Any<CancellationToken>()).Returns(user);
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(user);
        var calls = new List<IReadOnlyList<RateLimitCounter>>();
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(calls.Add), Arg.Any<CancellationToken>()).Returns(RateLimitDecision.Allowed);
        var service = CreateService();

        await service.LoginAsync("tom@example.com", "x", TestData.Origin, TestSupport.Ct);
        await service.LoginAsync("Tom", "x", TestData.Origin, TestSupport.Ct);
        await service.LoginAsync("nobody", "x", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountCounterKey(user), calls[0][1].Key);
        Assert.Equal(AccountCounterKey(user), calls[1][1].Key);
        // A name no account has is still counted, on a counter of its own.
        Assert.NotEqual(AccountCounterKey(user), calls[2][1].Key);
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
        Assert.Equal(TestData.Now, user.EmailVerifiedAt);
        Received.InOrder(() =>
        {
            sessions.RevokeAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>());
            sessions.ForgetAllAsync(user.Id, Arg.Any<CancellationToken>());
            store.ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        });
        // Tabs still open in the old sessions sign out.
        Assert.Equal([$$"""{"account_id":"{{user.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
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
        await store.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        // The account's other tabs switch language too.
        Assert.Equal([$$"""{"account_id":"{{user.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.AccountChanged));
    }

    [Fact]
    public async Task ThemeIsSavedOnlyForKnownSchemes()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var service = CreateService();

        var known = await service.SetThemeAsync(user.Id, "dark", TestSupport.Ct);
        var unknown = await service.SetThemeAsync(user.Id, "purple", TestSupport.Ct);

        Assert.True(known.IsSuccess);
        Assert.Equal("dark", user.Theme);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(1, unitOfWork.Saves);
        await store.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        // The account's other tabs switch in place, not shown anew.
        Assert.Equal([$$"""{"account_id":"{{user.Id:D}}","theme":"dark"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ThemeChanged));
        Assert.Empty(TestData.LiveEvents(liveEvents, LiveEventTypes.AccountChanged));
    }

    [Fact]
    public async Task AWrongResetCodeStaysCountedAndKeepsSessions()
    {
        var user = TestData.User();
        await IssueResetCodeAsync(user);

        var result = await CreateService().ResetPasswordAsync(new PasswordResetRequest(user.Email, "000000", "new password!"), TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        // Counted outside any transaction, so a rollback cannot undo it.
        Assert.Empty(unitOfWork.Locks);
        await sessions.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task VerifyingClearsTheCachedAccountState()
    {
        var user = TestData.User(verified: false);
        var (code, _) = await IssueVerificationCodeAsync(user);

        await CreateService().VerifyEmailAsync(user.Id, code, TestSupport.Ct);

        await store.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
    }

    // ---- Settings ----------------------------------------------------------

    [Fact]
    public async Task SettingsShowTheAccountAsStored()
    {
        var user = TestData.User();
        user.Actor.Bio = "hello";
        user.HideFollowCounts = true;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([Login(user.Id, ExternalProviders.GitHub, "42", "octocat")]);

        var settings = await CreateService().GetSettingsAsync(user.Id, TestSupport.Ct);
        var missing = await CreateService().GetSettingsAsync(Guid.NewGuid(), TestSupport.Ct);

        Assert.Equal(new AccountSettings("tom", "tom", "hello", "tom@example.com", null, "en", new ProfilePrivacy(false, false, true), true, settings.Value.Logins), settings.Value);
        Assert.Equal([new LinkedLogin(ExternalProviders.GitHub, "octocat")], settings.Value.Logins);
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task UpdatingTheProfileSavesAndDropsTheCachedState()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().UpdateProfileAsync(user.Id, "  Tom T.  ", "line 1\r\nline 2", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("Tom T.", "line 1\nline 2"), (user.Actor.DisplayName, user.Actor.Bio));
        Assert.Equal(1, unitOfWork.Saves);
        await store.Received(1).ForgetAccountAsync(user.Id, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, new SearchAccountSyncRequest(user.Id, false), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
        Assert.Equal([$$"""{"actor_id":"{{user.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task PrivacyIsSavedAndOpenProfilesCatchUp()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().SetPrivacyAsync(user.Id, new ProfilePrivacy(true, false, true), TestSupport.Ct);
        var missing = await CreateService().SetPrivacyAsync(Guid.NewGuid(), ProfilePrivacy.None, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((true, false, true), (user.HideLikes, user.HideFollows, user.HideFollowCounts));
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal([$$"""{"actor_id":"{{user.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.ActorChanged));
        Assert.Equal(ErrorKind.NotFound, missing.Error?.Kind);
    }

    [Fact]
    public async Task AnEmptyBioClearsIt()
    {
        var user = TestData.User();
        user.Actor.Bio = "old";
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await CreateService().UpdateProfileAsync(user.Id, "Tom", "   ", TestSupport.Ct);

        Assert.Null(user.Actor.Bio);
    }

    [Fact]
    public async Task ABadProfileNamesEveryBadField()
    {
        var result = await CreateService().UpdateProfileAsync(Guid.NewGuid(), " ", new string('x', 301), TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidInput, result.Error?.Code);
        Assert.Equal([ProfileFields.Bio, ProfileFields.DisplayName], result.Error!.Fields!.Keys.Order());
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task ChangingThePasswordKeepsThisSessionAndEndsTheOthers()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var current = Guid.NewGuid();

        var result = await CreateService().ChangePasswordAsync(user.Id, current, "old password", "new password!", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:new password!", user.PasswordHash);
        Assert.Equal(1, unitOfWork.Saves);
        Received.InOrder(() =>
        {
            sessions.RevokeOthersAsync(user.Id, current, TestData.Now, Arg.Any<CancellationToken>());
            sessions.ForgetAllAsync(user.Id, Arg.Any<CancellationToken>());
        });
        await sessions.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, TestSupport.Ct);
        // The tabs of the other sessions sign out.
        Assert.Equal([$$"""{"account_id":"{{user.Id:D}}"}"""], TestData.LiveEvents(liveEvents, LiveEventTypes.SessionsEnded));
    }

    [Fact]
    public async Task WithoutAKnownSessionEverySessionEnds()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await CreateService().ChangePasswordAsync(user.Id, null, "old password", "new password!", TestSupport.Ct);

        await sessions.Received(1).RevokeAllAsync(user.Id, TestData.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AWrongCurrentPasswordChangesNothing()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ChangePasswordAsync(user.Id, Guid.NewGuid(), "guess", "new password!", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidCredentials, result.Error?.Code);
        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
        Assert.Equal(["Error.WrongPassword"], result.Error?.Fields?[AccountFields.CurrentPassword]!);
        Assert.Equal("hashed:old password", user.PasswordHash);
        await sessions.DidNotReceiveWithAnyArgs().RevokeOthersAsync(default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task PasswordGuessesShareTheAccountsSignInLimit()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        IReadOnlyList<RateLimitCounter>? counted = null;
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(c => counted = c), Arg.Any<CancellationToken>())
            .Returns(RateLimitDecision.Denied(TimeSpan.FromMinutes(3)));

        var result = await CreateService().ChangePasswordAsync(user.Id, Guid.NewGuid(), "old password", "new password!", TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        // The same counter a sign-in to the account uses, by address or username.
        Assert.Equal(AccountCounterKey(user), Assert.Single(counted!).Key);
        hasher.DidNotReceiveWithAnyArgs().VerifyHashedPassword(default!, default!, default!);
    }

    [Fact]
    public async Task ANewPasswordMustFollowThePasswordRules()
    {
        var result = await CreateService().ChangePasswordAsync(Guid.NewGuid(), null, "", "short", TestSupport.Ct);

        Assert.Equal([AccountFields.NewPassword], result.Error!.Fields!.Keys);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnAccountWithAPasswordMustGiveIt()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ChangePasswordAsync(user.Id, null, "", "new password!", TestSupport.Ct);

        Assert.Equal([AccountFields.CurrentPassword], result.Error!.Fields!.Keys);
        Assert.Equal("hashed:old password", user.PasswordHash);
    }

    [Fact]
    public async Task AnAccountWithoutAPasswordSetsItsFirstWithoutOne()
    {
        var user = TestData.User();
        user.PasswordHash = null;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var keep = Guid.NewGuid();

        var result = await CreateService().ChangePasswordAsync(user.Id, keep, null, "new password!", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:new password!", user.PasswordHash);
        await sessions.Received(1).RevokeOthersAsync(user.Id, keep, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        // Counted as a change to the account; there is no password to guess.
        await rateLimitStore.Received().TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.All(x => x.Key.StartsWith("AccountChange:", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
        hasher.DidNotReceiveWithAnyArgs().VerifyHashedPassword(default!, default!, default!);
    }

    [Fact]
    public async Task AnAccountWithoutAPasswordCannotBeSignedInToWithOne()
    {
        var user = TestData.User();
        user.PasswordHash = null;
        store.FindUserByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().LoginAsync("tom", "anything", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.InvalidCredentials, result.Error?.Code);
    }

    // ---- Email change ------------------------------------------------------

    [Fact]
    public async Task AskingToChangeTheEmailSendsACodeToTheNewAddressAndChangesNothingYet()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        EmailCode? stored = null;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());

        var result = await CreateService().RequestEmailChangeAsync(user.Id, " New@Example.org ", "old password", TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("tom@example.com", "New@Example.org"), (user.NormalizedEmail, user.PendingEmail));
        var email = Assert.Single(sentCodes);
        Assert.Equal((EmailCodePurpose.ChangeEmail, "New@Example.org"), (email.Purpose, email.To));
        // Kept under the account's own address: one move at a time, and nobody else's code is replaced.
        Assert.Equal((EmailCodePurpose.ChangeEmail, "tom@example.com"), (stored?.Purpose, stored?.NormalizedEmail));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task AWrongPasswordSendsNoCode()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "new@example.org", "guess", TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Error.WrongPassword"], result.Error?.Fields?[AccountFields.CurrentPassword]!);
        Assert.Empty(sentCodes);
        Assert.Null(user.PendingEmail);
    }

    [Fact]
    public async Task EmailChangePasswordGuessesShareTheAccountsSignInLimit()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        IReadOnlyList<RateLimitCounter>? counted = null;
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(c => counted = c), Arg.Any<CancellationToken>())
            .Returns(RateLimitDecision.Denied(TimeSpan.FromMinutes(3)));

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "new@example.org", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        Assert.Equal(AccountCounterKey(user), Assert.Single(counted!).Key);
        hasher.DidNotReceiveWithAnyArgs().VerifyHashedPassword(default!, default!, default!);
    }

    [Fact]
    public async Task CodesToTheNewAddressCountAgainstItsLimits()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var calls = new List<IReadOnlyList<RateLimitCounter>>();
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(calls.Add), Arg.Any<CancellationToken>())
            .Returns(RateLimitDecision.Allowed, RateLimitDecision.Denied(TimeSpan.FromMinutes(1)));

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "new@example.org", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        Assert.Equal([RateLimitPolicies.EmailCodeIp, RateLimitPolicies.EmailCode, RateLimitPolicies.EmailCodeAccount], TestData.Policies(calls[^1]));
        Assert.Empty(sentCodes);
        Assert.Null(user.PendingEmail);
    }

    [Fact]
    public async Task ABadNewAddressIsRefusedBeforeLookingAtTheAccount()
    {
        var result = await CreateService().RequestEmailChangeAsync(Guid.NewGuid(), "not-an-email", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal([AccountFields.NewEmail], result.Error!.Fields!.Keys);
        await store.DidNotReceiveWithAnyArgs().FindUserAsync(default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnEmailChangeNeedsThePasswordOfAnAccountThatHasOne()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "new@example.org", "", TestData.Origin, TestSupport.Ct);

        Assert.Equal([AccountFields.CurrentPassword], result.Error!.Fields!.Keys);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task AnAccountWithoutAPasswordChangesItsEmailWithoutOne()
    {
        var user = TestData.User();
        user.PasswordHash = null;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "new@example.org", null, TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("new@example.org", user.PendingEmail);
        Assert.Equal("new@example.org", Assert.Single(sentCodes).To);
    }

    [Fact]
    public async Task TheSameAddressIsNotAChange()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "TOM@example.com", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Validation.SameEmail"], result.Error?.Fields?[AccountFields.NewEmail]!);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task AnAddressWithAnAccountCannotBeMovedTo()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.EmailTakenAsync("alice@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RequestEmailChangeAsync(user.Id, "alice@example.com", "old password", TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.EmailTaken, result.Error?.Code);
        Assert.Equal(["Error.EmailTaken"], result.Error?.Fields?[AccountFields.NewEmail]!);
        Assert.Empty(sentCodes);
    }

    [Fact]
    public async Task TheCodeMovesTheAccountToTheNewAddress()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (code, stored) = await IssueEmailChangeCodeAsync(user, "New@Example.org");

        var result = await CreateService().ConfirmEmailChangeAsync(user.Id, code, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("New@Example.org", "new@example.org", null), (user.Email, user.NormalizedEmail, user.PendingEmail));
        store.Received(1).RemoveEmailCode(stored);
        // The admin user list shows the address.
        Assert.Single(TestData.LiveEvents(liveEvents, LiveEventTypes.AdminChanged));
    }

    [Fact]
    public async Task AWrongCodeLeavesTheEmailAsItWas()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (code, _) = await IssueEmailChangeCodeAsync(user, "new@example.org");

        var result = await CreateService().ConfirmEmailChangeAsync(user.Id, code == "000000" ? "111111" : "000000", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        Assert.Equal(("tom@example.com", "new@example.org"), (user.NormalizedEmail, user.PendingEmail));
        await store.Received(1).TryCountCodeAttemptAsync(EmailCodePurpose.ChangeEmail, "tom@example.com", TestData.Now, AccountService.MaxCodeAttempts, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNothingAskedForNoCodeWorks()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ConfirmEmailChangeAsync(user.Id, "123456", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, result.Error?.Code);
        await store.DidNotReceiveWithAnyArgs().TryCountCodeAttemptAsync(default, default!, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task AnAddressTakenSinceTheCodeWasSentIsRefusedWithoutUsingAGuess()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (code, _) = await IssueEmailChangeCodeAsync(user, "new@example.org");
        store.EmailTakenAsync("new@example.org", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().ConfirmEmailChangeAsync(user.Id, code, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.EmailTaken, result.Error?.Code);
        Assert.Equal("tom@example.com", user.NormalizedEmail);
        await store.DidNotReceiveWithAnyArgs().TryCountCodeAttemptAsync(default, default!, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task TheSettingsShowAMoveOnlyWhileItsCodeCanBeEntered()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (_, stored) = await IssueEmailChangeCodeAsync(user, "new@example.org");

        var waiting = await CreateService().GetSettingsAsync(user.Id, TestSupport.Ct);
        stored.Attempts = AccountService.MaxCodeAttempts;
        var spent = await CreateService().GetSettingsAsync(user.Id, TestSupport.Ct);
        stored.Attempts = 0;
        stored.ExpiresAt = TestData.Now;
        var expired = await CreateService().GetSettingsAsync(user.Id, TestSupport.Ct);

        Assert.Equal(new CodeSent("new@example.org", TestData.Now + AccountService.ResendInterval), waiting.Value.PendingEmail);
        Assert.Null(spent.Value.PendingEmail);
        Assert.Null(expired.Value.PendingEmail);
    }

    [Fact]
    public async Task TheCodeForAMoveIsSentAgainToTheSameAddressAfterAMinute()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (_, stored) = await IssueEmailChangeCodeAsync(user, "New@Example.org");
        var tooSoon = await CreateService().ResendEmailChangeAsync(user.Id, TestData.Origin, TestSupport.Ct);
        stored.CreatedAt = TestData.Now - AccountService.ResendInterval;
        stored.Attempts = 3;

        var result = await CreateService().ResendEmailChangeAsync(user.Id, TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, tooSoon.Error?.Kind);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, sentCodes.Count);
        Assert.Equal((EmailCodePurpose.ChangeEmail, "New@Example.org"), (sentCodes[^1].Purpose, sentCodes[^1].To));
        Assert.Equal((0, TestData.Now), (stored.Attempts, stored.CreatedAt));
    }

    [Fact]
    public async Task WithNoMoveUnderWayThereIsNothingToSendAgain()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (_, stored) = await IssueEmailChangeCodeAsync(user, "new@example.org");
        stored.ExpiresAt = TestData.Now;
        var other = TestData.User("ann");
        store.FindUserAsync(other.Id, Arg.Any<CancellationToken>()).Returns(other);

        var expired = await CreateService().ResendEmailChangeAsync(user.Id, TestData.Origin, TestSupport.Ct);
        var none = await CreateService().ResendEmailChangeAsync(other.Id, TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.CodeInvalid, expired.Error?.Code);
        Assert.Equal(AccountErrorCodes.CodeInvalid, none.Error?.Code);
        Assert.Single(sentCodes);
    }

    [Fact]
    public async Task StartingOverDropsTheMoveAndItsCode()
    {
        var user = TestData.User(passwordHash: "hashed:old password");
        var (_, stored) = await IssueEmailChangeCodeAsync(user, "new@example.org");

        var result = await CreateService().CancelEmailChangeAsync(user.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(user.PendingEmail);
        store.Received(1).RemoveEmailCode(stored);
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

    private async Task<(string Code, EmailCode Stored)> IssueEmailChangeCodeAsync(User user, string newEmail)
    {
        EmailCode? stored = null;
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.When(s => s.AddEmailCode(Arg.Any<EmailCode>())).Do(ci => stored = ci.Arg<EmailCode>());
        await CreateService().RequestEmailChangeAsync(user.Id, newEmail, "old password", TestData.Origin, TestSupport.Ct);
        store.FindEmailCodeAsync(EmailCodePurpose.ChangeEmail, user.NormalizedEmail, Arg.Any<CancellationToken>()).Returns(stored);
        return (Unprotect(sentCodes[^1]), stored!);
    }

    /// <summary>The account's sign-in counter, as the store sees it (keys hold a hash of the partition).</summary>
    private static string AccountCounterKey(User user)
    {
        var partition = System.Text.Encoding.UTF8.GetBytes(RateLimitPartitions.Account(user.Id));
        return $"{RateLimitPolicies.Login}:60:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(partition).AsSpan(0, 16))}";
    }

    private string Unprotect(AccountCodeEmail email) =>
        dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose).Unprotect(email.ProtectedCode);

    private static ExternalLogin Login(Guid userId, string provider, string key, string name) =>
        new() { UserId = userId, Provider = provider, ProviderKey = key, Name = name, CreatedAt = TestData.Now };

    private AccountService CreateService() => new(
        store,
        sessions,
        unitOfWork,
        outbox,
        captcha,
        hasher,
        TestData.RateLimits(rateLimitStore),
        TestData.Live(liveEvents),
        dataProtection,
        TestData.Time(),
        new TestLocalizer(),
        NullLogger<AccountService>.Instance);
}
