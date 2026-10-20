using Beeps.Features.Accounts;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Accounts;

public sealed partial class AccountServiceTests
{
    private const string Credential = """{"id":"AQID"}""";

    private static readonly byte[] CredentialId = [1, 2, 3];

    // ---- Adding and removing passkeys ---------------------------------------------------------

    [Fact]
    public async Task AddingAPasskeyKeepsWhatTheAuthenticatorMadeNamedAfterWhereItWasAdded()
    {
        var user = ArrangePasskeyUser();
        ceremonies.BeginRegistrationAsync(user.Id, "tom", "tom", Arg.Any<IReadOnlyList<Passkey>>()).Returns(new PasskeyChallenge("{\"options\":1}", "state"));
        ceremonies.FinishRegistrationAsync(Credential, "state", Arg.Any<CancellationToken>())
            .Returns(new NewPasskey(CredentialId, [9, 9], 0, ["internal"], true, true));
        Passkey? added = null;
        store.When(s => s.AddPasskey(Arg.Any<Passkey>())).Do(ci => added = ci.Arg<Passkey>());

        var request = await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct);
        var result = await CreateService().AddPasskeyAsync(user.Id, Credential, request.Value.Ticket, "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari/605.1.15", TestSupport.Ct);

        Assert.Equal("{\"options\":1}", request.Value.OptionsJson);
        Assert.DoesNotContain("state", request.Value.Ticket, StringComparison.Ordinal);
        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(user.Id, added.UserId);
        Assert.Equal(CredentialId, added.CredentialId);
        Assert.Equal([9, 9], added.PublicKey);
        Assert.Equal(["internal"], added.Transports ?? []);
        Assert.True(added.BackupEligible);
        Assert.Equal(TestData.Now, added.CreatedAt);
        Assert.Equal(new DeviceInfo(DeviceKind.Computer, "Mac", "Safari"), UserAgents.Describe(added.UserAgent));
        Assert.Equal(1, unitOfWork.Saves);
        // The owner hears of it, saved with it: a stolen session could otherwise add a way back in that outlives a password reset.
        outbox.Received(1).Enqueue(SignInMethodEmail.MessageType, new SignInMethodEmail(user.Email, user.Locale, null), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SignInMethodEmail>>());
    }

    [Fact]
    public async Task ATicketForAnotherAccountOrTooOldAddsNothing()
    {
        var user = ArrangePasskeyUser();
        var other = ArrangePasskeyUser("alice");
        ceremonies.BeginRegistrationAsync(default, default!, default!, default!).ReturnsForAnyArgs(new PasskeyChallenge("{}", "state"));
        ceremonies.FinishRegistrationAsync(default!, default!, TestSupport.Ct).ReturnsForAnyArgs(new NewPasskey(CredentialId, [9], 0, null, false, false));
        var ticket = (await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct)).Value.Ticket;
        var later = new FixedTimeProvider(TestData.Now + AccountService.PasskeyTicketLifetime);

        var forOther = await CreateService().AddPasskeyAsync(other.Id, Credential, ticket, null, TestSupport.Ct);
        var tooOld = await CreateService(later).AddPasskeyAsync(user.Id, Credential, ticket, null, TestSupport.Ct);
        var forged = await CreateService().AddPasskeyAsync(user.Id, Credential, "state", null, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeyFailed, forOther.Error?.Code);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, tooOld.Error?.Code);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, forged.Error?.Code);
        await ceremonies.DidNotReceiveWithAnyArgs().FinishRegistrationAsync(default!, default!, TestSupport.Ct);
        store.DidNotReceiveWithAnyArgs().AddPasskey(default!);
    }

    [Fact]
    public async Task AnAnswerThatDoesNotHoldUpAddsNothing()
    {
        var user = ArrangePasskeyUser();
        ceremonies.BeginRegistrationAsync(default, default!, default!, default!).ReturnsForAnyArgs(new PasskeyChallenge("{}", "state"));
        var ticket = (await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct)).Value.Ticket;

        var result = await CreateService().AddPasskeyAsync(user.Id, Credential, ticket, null, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeyFailed, result.Error?.Code);
        store.DidNotReceiveWithAnyArgs().AddPasskey(default!);
    }

    [Fact]
    public async Task ThePasskeySentTwiceIsAddedOnceAndAnothersIsRefused()
    {
        var user = ArrangePasskeyUser();
        ceremonies.BeginRegistrationAsync(default, default!, default!, default!).ReturnsForAnyArgs(new PasskeyChallenge("{}", "state"));
        ceremonies.FinishRegistrationAsync(default!, default!, TestSupport.Ct).ReturnsForAnyArgs(new NewPasskey(CredentialId, [9], 0, null, false, false));
        var ticket = (await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct)).Value.Ticket;

        store.FindPasskeyAsync(CredentialId, Arg.Any<CancellationToken>()).Returns(StoredPasskey(user.Id));
        var again = await CreateService().AddPasskeyAsync(user.Id, Credential, ticket, null, TestSupport.Ct);
        store.FindPasskeyAsync(CredentialId, Arg.Any<CancellationToken>()).Returns(StoredPasskey(Guid.NewGuid()));
        var anothers = await CreateService().AddPasskeyAsync(user.Id, Credential, ticket, null, TestSupport.Ct);

        Assert.True(again.IsSuccess);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, anothers.Error?.Code);
        store.DidNotReceiveWithAnyArgs().AddPasskey(default!);
    }

    [Fact]
    public async Task AnAccountHasAtMostSoManyPasskeys()
    {
        var user = ArrangePasskeyUser();
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, AccountService.MaxPasskeys).Select(_ => StoredPasskey(user.Id))]);

        var result = await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeysFull, result.Error?.Code);
        await ceremonies.DidNotReceiveWithAnyArgs().BeginRegistrationAsync(default, default!, default!, default!);
    }

    [Fact]
    public async Task AddingIsRateLimitedPerAccount()
    {
        var user = ArrangePasskeyUser();
        ceremonies.BeginRegistrationAsync(default, default!, default!, default!).ReturnsForAnyArgs(new PasskeyChallenge("{}", "state"));
        var ticket = (await CreateService().BeginAddPasskeyAsync(user.Id, TestSupport.Ct)).Value.Ticket;
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(5)));

        var result = await CreateService().AddPasskeyAsync(user.Id, Credential, ticket, null, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        await ceremonies.DidNotReceiveWithAnyArgs().FinishRegistrationAsync(default!, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task RemovingTheLastPasskeyEndsTwoStepSignIn()
    {
        var user = ArrangePasskeyUser();
        user.PasskeyTwoFactor = true;
        var passkey = StoredPasskey(user.Id);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([passkey]);

        var result = await CreateService().RemovePasskeyAsync(user.Id, passkey.Id, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        store.Received(1).RemovePasskey(passkey);
        Assert.False(user.PasskeyTwoFactor);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task RemovingAPasskeyThatIsGoneOrSomeoneElsesDoesNothing()
    {
        var user = ArrangePasskeyUser();
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);

        var result = await CreateService().RemovePasskeyAsync(user.Id, Guid.NewGuid(), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        store.DidNotReceiveWithAnyArgs().RemovePasskey(default!);
    }

    [Fact]
    public async Task TheLastPasskeyStaysWhileItIsTheOnlyWayToSignIn()
    {
        var user = ArrangePasskeyUser();
        user.PasswordHash = null;
        var passkey = StoredPasskey(user.Id);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([passkey]);

        var alone = await CreateService().RemovePasskeyAsync(user.Id, passkey.Id, TestSupport.Ct);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat")]);
        var withGitHub = await CreateService().RemovePasskeyAsync(user.Id, passkey.Id, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.LastSignInMethod, alone.Error?.Code);
        Assert.True(withGitHub.IsSuccess);
        store.Received(1).RemovePasskey(passkey);
    }

    [Fact]
    public async Task APasskeyThatSignsInByItselfLetsTheLastProviderGo()
    {
        var user = ArrangePasskeyUser();
        user.PasswordHash = null;
        var github = Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat");
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([github]);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);

        user.PasskeyTwoFactor = true;
        var secondStepOnly = await CreateService().UnlinkExternalAsync(user.Id, "github", TestSupport.Ct);
        user.PasskeyTwoFactor = false;
        var byItself = await CreateService().UnlinkExternalAsync(user.Id, "github", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.LastSignInMethod, secondStepOnly.Error?.Code);
        Assert.True(byItself.IsSuccess);
        store.Received(1).RemoveLogin(github);
    }

    [Fact]
    public async Task TwoStepSignInNeedsAPasskeyAndAFirstStep()
    {
        var user = ArrangePasskeyUser();

        var noPasskeys = await CreateService().SetPasskeyTwoFactorAsync(user.Id, true, TestSupport.Ct);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);
        user.PasswordHash = null;
        var noFirstStep = await CreateService().SetPasskeyTwoFactorAsync(user.Id, true, TestSupport.Ct);
        user.PasswordHash = "hash";
        var on = await CreateService().SetPasskeyTwoFactorAsync(user.Id, true, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.NoPasskeys, noPasskeys.Error?.Code);
        Assert.Equal(AccountErrorCodes.LastSignInMethod, noFirstStep.Error?.Code);
        Assert.True(on.IsSuccess);
        Assert.True(user.PasskeyTwoFactor);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task ThePasskeysAreListedWithWhereTheyWereAdded()
    {
        var user = ArrangePasskeyUser();
        var passkey = StoredPasskey(user.Id, "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148 Safari/604.1");
        passkey.LastUsedAt = TestData.Now;
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([passkey]);

        var listed = Assert.Single(await CreateService().ListPasskeysAsync(user.Id, TestSupport.Ct));

        Assert.Equal(new PasskeySummary(passkey.Id, new DeviceInfo(DeviceKind.Phone, "iPhone", "Safari"), passkey.CreatedAt, TestData.Now), listed);
    }

    // ---- Signing in ---------------------------------------------------------------------------

    [Fact]
    public async Task APasskeySignsInItsAccountAndKeepsWhatItSaid()
    {
        var user = ArrangePasskeyUser();
        var passkey = ArrangeSigningPasskey(user);
        var ticket = await SignInTicketAsync();
        var calls = new List<IReadOnlyList<RateLimitCounter>>();
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(calls.Add), Arg.Any<CancellationToken>()).Returns(RateLimitDecision.Allowed);

        var result = await CreateService().PasskeySignInAsync(Credential, ticket, TestData.Origin, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(7, passkey.SignCount);
        Assert.True(passkey.BackedUp);
        Assert.Equal(TestData.Now, passkey.LastUsedAt);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal([RateLimitPolicies.Login], TestData.Policies(Assert.Single(calls)));
    }

    [Fact]
    public async Task AnAnswerSentAgainDoesNotSignInASecondTime()
    {
        var user = ArrangePasskeyUser();
        ArrangeSigningPasskey(user);
        var ticket = await SignInTicketAsync();
        var later = new FixedTimeProvider(TestData.Now + TimeSpan.FromMinutes(1));

        var first = await CreateService(later).PasskeySignInAsync(Credential, ticket, TestData.Origin, TestSupport.Ct);
        var replayed = await CreateService(later).PasskeySignInAsync(Credential, ticket, TestData.Origin, TestSupport.Ct);
        var fresh = await CreateService(later).PasskeySignInAsync(Credential, await SignInTicketAsync(later), TestData.Origin, TestSupport.Ct);

        Assert.True(first.IsSuccess);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, replayed.Error?.Code);
        Assert.True(fresh.IsSuccess);
    }

    [Fact]
    public async Task AnAccountThatKeepsItsPasskeysForTheSecondStepIsNotSignedInByOne()
    {
        var user = ArrangePasskeyUser();
        user.PasskeyTwoFactor = true;
        var passkey = ArrangeSigningPasskey(user);

        var result = await CreateService().PasskeySignInAsync(Credential, await SignInTicketAsync(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeyTwoFactor, result.Error?.Code);
        Assert.Null(passkey.LastUsedAt);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task ABannedAccountIsNotSignedInByItsPasskey()
    {
        var user = ArrangePasskeyUser();
        user.BannedAt = TestData.Now;
        ArrangeSigningPasskey(user);

        var result = await CreateService().PasskeySignInAsync(Credential, await SignInTicketAsync(), TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AccountBanned, result.Error?.Code);
    }

    [Fact]
    public async Task APasskeyNobodyHasOrThatDidNotSignSignsNobodyIn()
    {
        var user = ArrangePasskeyUser();
        var ticket = await SignInTicketAsync();
        ceremonies.CredentialIdOf(Credential).Returns(CredentialId);

        var unknown = await CreateService().PasskeySignInAsync(Credential, ticket, TestData.Origin, TestSupport.Ct);
        store.FindPasskeyAsync(CredentialId, Arg.Any<CancellationToken>()).Returns(StoredPasskey(user.Id));
        var unsigned = await CreateService().PasskeySignInAsync(Credential, ticket, TestData.Origin, TestSupport.Ct);
        var noTicket = await CreateService().PasskeySignInAsync(Credential, null, TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeyFailed, unknown.Error?.Code);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, unsigned.Error?.Code);
        Assert.Equal(AccountErrorCodes.PasskeyFailed, noTicket.Error?.Code);
    }

    [Fact]
    public async Task PasskeySignInsAreRateLimitedPerIp()
    {
        rateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromMinutes(5)));

        var result = await CreateService().PasskeySignInAsync(Credential, "ticket", TestData.Origin, TestSupport.Ct);

        Assert.Equal(ErrorKind.RateLimited, result.Error?.Kind);
        ceremonies.DidNotReceiveWithAnyArgs().CredentialIdOf(default!);
    }

    [Fact]
    public async Task OnlyAnAccountThatAsksForItHasASecondStep()
    {
        var user = ArrangePasskeyUser();

        var off = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);
        user.PasskeyTwoFactor = true;
        var noPasskeys = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);
        var on = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);

        Assert.Null(off);
        Assert.Null(noPasskeys);
        Assert.NotNull(on);
        Assert.DoesNotContain(user.Id.ToString(), on, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSecondStepAsksForTheAccountsPasskeysAndSignsIn()
    {
        var user = ArrangePasskeyUser();
        user.PasskeyTwoFactor = true;
        var passkey = ArrangeSigningPasskey(user);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([passkey]);
        ceremonies.BeginAssertionAsync(user.Id, Arg.Is<IReadOnlyList<Passkey>>(p => p.Single() == passkey)).Returns(new PasskeyChallenge("{}", "state"));
        var firstStep = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);
        var calls = new List<IReadOnlyList<RateLimitCounter>>();
        rateLimitStore.TryAcquireAsync(Arg.Do<IReadOnlyList<RateLimitCounter>>(calls.Add), Arg.Any<CancellationToken>()).Returns(RateLimitDecision.Allowed);

        var request = await CreateService().BeginSecondStepAsync(firstStep, TestSupport.Ct);
        var result = await CreateService().FinishSecondStepAsync(firstStep, Credential, request.Value.Ticket, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(TestData.Now, passkey.LastUsedAt);
        Assert.Equal([RateLimitPolicies.Login], TestData.Policies(Assert.Single(calls)));
    }

    [Fact]
    public async Task TheSecondStepRefusesAnotherAccountsPasskey()
    {
        var user = ArrangePasskeyUser();
        user.PasskeyTwoFactor = true;
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);
        var alice = ArrangePasskeyUser("alice");
        ArrangeSigningPasskey(alice);
        ceremonies.BeginAssertionAsync(default, default!).ReturnsForAnyArgs(new PasskeyChallenge("{}", "state"));
        var firstStep = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);
        var request = await CreateService().BeginSecondStepAsync(firstStep, TestSupport.Ct);

        var result = await CreateService().FinishSecondStepAsync(firstStep, Credential, request.Value.Ticket, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.PasskeyFailed, result.Error?.Code);
        await ceremonies.DidNotReceiveWithAnyArgs().FinishAssertionAsync(default!, default!, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task TheSecondStepMustFollowTheFirstSoon()
    {
        var user = ArrangePasskeyUser();
        user.PasskeyTwoFactor = true;
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([StoredPasskey(user.Id)]);
        var firstStep = await CreateService().StartSecondStepAsync(user.Id, TestSupport.Ct);
        var later = new FixedTimeProvider(TestData.Now + AccountService.SecondStepLifetime);

        var tooLate = await CreateService(later).BeginSecondStepAsync(firstStep, TestSupport.Ct);
        var forged = await CreateService().BeginSecondStepAsync(user.Id.ToString("D"), TestSupport.Ct);
        var signInTicket = await CreateService().BeginSecondStepAsync(await SignInTicketAsync(), TestSupport.Ct);

        Assert.Equal("Error.SecondStepExpired", tooLate.Error?.Detail);
        Assert.Equal("Error.SecondStepExpired", forged.Error?.Detail);
        Assert.Equal("Error.SecondStepExpired", signInTicket.Error?.Detail);
    }

    private User ArrangePasskeyUser(string handle = "tom")
    {
        var user = TestData.User(handle);
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListPasskeysAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        return user;
    }

    /// <summary>A passkey of the account's, which the browser's answer names and which signs.</summary>
    private Passkey ArrangeSigningPasskey(User user)
    {
        var passkey = StoredPasskey(user.Id);
        ceremonies.CredentialIdOf(Credential).Returns(CredentialId);
        store.FindPasskeyAsync(CredentialId, Arg.Any<CancellationToken>()).Returns(passkey);
        ceremonies.FinishAssertionAsync(Credential, "state", passkey, Arg.Any<CancellationToken>()).Returns(new PasskeyUse(7, true));
        return passkey;
    }

    private async Task<string> SignInTicketAsync(TimeProvider? at = null)
    {
        ceremonies.BeginAssertionAsync(null, Arg.Any<IReadOnlyList<Passkey>>()).Returns(new PasskeyChallenge("{}", "state"));
        return (await CreateService(at).BeginPasskeySignInAsync()).Ticket;
    }

    private static Passkey StoredPasskey(Guid userId, string? userAgent = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        CredentialId = CredentialId,
        PublicKey = [9],
        UserAgent = userAgent,
        CreatedAt = TestData.Now,
    };
}
