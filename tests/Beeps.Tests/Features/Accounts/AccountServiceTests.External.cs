using Beeps.Features.Accounts;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Accounts;

public sealed partial class AccountServiceTests
{
    private static readonly ExternalIdentity Octocat = new(ExternalProviders.GitHub, "42", "octo@example.com", "The Octocat", "octo-cat");

    // ---- Signing in with a provider ----------------------------------------------------------

    [Fact]
    public async Task ALinkedProviderSignsInItsAccount()
    {
        var user = TestData.User();
        store.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>()).Returns(Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat"));
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.Account?.UserId);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task ALinkToADeletedAccountSignsNobodyIn()
    {
        store.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>()).Returns(Login(Guid.NewGuid(), ExternalProviders.GitHub, "42", "octo-cat"));

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
        Assert.Equal("Error.ExternalAccountGone(GitHub)", result.Error?.Detail);
    }

    [Fact]
    public async Task ABannedAccountCannotSignInWithAProvider()
    {
        var user = TestData.User();
        user.BannedAt = TestData.Now;
        store.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>()).Returns(Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat"));
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.AccountBanned, result.Error?.Code);
    }

    [Fact]
    public async Task TheAccountWithTheVerifiedAddressIsLinkedAndSignedIn()
    {
        var user = TestData.User("octo");
        store.FindUserByEmailAsync("octo@example.com", Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.Account?.UserId);
        store.Received(1).AddLogin(Arg.Is<ExternalLogin>(l => l.UserId == user.Id && l.Provider == ExternalProviders.GitHub && l.ProviderKey == "42" && l.Name == "octo-cat"));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task AnAddressNeverVerifiedHereIsNotLinked()
    {
        // Whoever typed it in at sign-up set the password; linking would let them in with the provider's account too.
        var user = TestData.User("octo", verified: false);
        store.FindUserByEmailAsync("octo@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.EmailTaken, result.Error?.Code);
        store.DidNotReceiveWithAnyArgs().AddLogin(default!);
    }

    [Fact]
    public async Task SomeoneNewIsOfferedTheirUsernameThere()
    {
        store.HandleTakenAsync("octo_cat", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Null(result.Value.Account);
        Assert.Equal("octo_cat2", result.Value.SuggestedUsername);
    }

    [Fact]
    public async Task AThrowawayAddressIsNotOfferedAnAccount()
    {
        var result = await CreateService().ExternalSignInAsync(Octocat with { Email = "octo@mailinator.com" }, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.DisposableEmail, result.Error?.Code);
        Assert.Equal("Error.ExternalDisposableEmail(GitHub)", result.Error?.Detail);
    }

    [Fact]
    public async Task AThrowawayAddressWithAnAccountStillSignsIn()
    {
        var user = TestData.User("octo");
        store.FindUserByEmailAsync("octo@mailinator.com", Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateService().ExternalSignInAsync(Octocat with { Email = "octo@mailinator.com" }, TestSupport.Ct);

        Assert.Equal(user.Id, result.Value.Account?.UserId);
    }

    [Fact]
    public async Task WithoutAUsernameThereTheAddressSuggestsOne()
    {
        var result = await CreateService().ExternalSignInAsync(Octocat with { Provider = ExternalProviders.Google, Username = null, Email = "jane.doe@example.com" }, TestSupport.Ct);

        Assert.Equal("jane_doe", result.Value.SuggestedUsername);
    }

    [Fact]
    public async Task NoVerifiedAddressNoAccount()
    {
        var result = await CreateService().ExternalSignInAsync(Octocat with { Email = null }, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.ExternalNoEmail, result.Error?.Code);
    }

    [Fact]
    public async Task ClosedRegistrationTurnsNewcomersAway()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.Closed);

        var result = await CreateService().ExternalSignInAsync(Octocat, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.RegistrationClosed, result.Error?.Code);
    }

    // ---- Signing up with a provider -----------------------------------------------------------

    [Fact]
    public async Task SigningUpMakesAVerifiedAccountWithNoPasswordAndNoCode()
    {
        store.AnyUsersAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RegisterExternalAsync(Octocat, "octo", null, TestData.Origin, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EmailVerified);
        store.Received(1).AddUser(Arg.Is<User>(u => u.Actor.Handle == "octo" && u.Actor.DisplayName == "The Octocat"
            && u.NormalizedEmail == "octo@example.com" && u.PasswordHash == null && u.EmailVerifiedAt == TestData.Now && !u.IsAdmin));
        store.Received(1).AddLogin(Arg.Is<ExternalLogin>(l => l.UserId == result.Value.UserId && l.ProviderKey == "42"));
        outbox.Received(1).Enqueue(SearchAccountSyncRequest.MessageType, Arg.Any<SearchAccountSyncRequest>(), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SearchAccountSyncRequest>>());
        Assert.Empty(sentCodes);
        await captcha.DidNotReceiveWithAnyArgs().VerifyAsync(default, default!, TestSupport.Ct);
        await rateLimitStore.Received(1).TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.All(x => x.Key.StartsWith("Register:", StringComparison.Ordinal))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFirstAccountSignedUpWithAProviderIsAdmin()
    {
        store.AnyUsersAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().RegisterExternalAsync(Octocat, "octo", null, TestData.Origin, TestSupport.Ct);

        Assert.True(result.Value.IsAdmin);
    }

    [Theory]
    [InlineData("a.b", "Validation.Username(3,20)")]
    [InlineData("admin", "Validation.UsernameReserved")]
    [InlineData("taken", "Error.UsernameTaken")]
    public async Task TheUsernameIsCheckedAsAtSignUp(string username, string message)
    {
        store.HandleTakenAsync("taken", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RegisterExternalAsync(Octocat, username, null, TestData.Origin, TestSupport.Ct);

        Assert.Equal([message], result.Error?.Fields?[AccountFields.Username]!);
        store.DidNotReceiveWithAnyArgs().AddUser(default!);
    }

    [Fact]
    public async Task AThrowawayAddressCannotSignUpWithAProvider()
    {
        var result = await CreateService().RegisterExternalAsync(Octocat with { Email = "octo@yopmail.com" }, "octo", null, TestData.Origin, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.DisposableEmail, result.Error?.Code);
        store.DidNotReceiveWithAnyArgs().AddUser(default!);
    }

    [Fact]
    public async Task InviteOnlyNeedsAnInviteWithAProviderToo()
    {
        store.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);

        var result = await CreateService().RegisterExternalAsync(Octocat, "octo", null, TestData.Origin, TestSupport.Ct);

        Assert.Equal(["Validation.InviteRequired"], result.Error?.Fields?[AccountFields.InviteCode]!);
    }

    [Fact]
    public async Task AnAddressWithAnAccountIsSignedInToNotSignedUpWith()
    {
        store.EmailTakenAsync("octo@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().RegisterExternalAsync(Octocat, "octo", null, TestData.Origin, TestSupport.Ct);

        Assert.Equal("Error.ExternalEmailTaken(GitHub)", result.Error?.Detail);
        Assert.Null(result.Error?.Fields);
    }

    // ---- Linking and unlinking from the settings ----------------------------------------------

    [Fact]
    public async Task TheSettingsLinkAProvider()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateService().LinkExternalAsync(user.Id, Octocat, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        store.Received(1).AddLogin(Arg.Is<ExternalLogin>(l => l.UserId == user.Id && l.ProviderKey == "42"));
        // The owner hears of it: a stolen session could otherwise add a way back in that outlives a password reset.
        outbox.Received(1).Enqueue(SignInMethodEmail.MessageType, new SignInMethodEmail(user.Email, user.Locale, "GitHub"), Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<SignInMethodEmail>>());
    }

    [Fact]
    public async Task AProviderAccountLinksToOneAccountOnly()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>()).Returns(Login(Guid.NewGuid(), ExternalProviders.GitHub, "42", "octo-cat"));

        var result = await CreateService().LinkExternalAsync(user.Id, Octocat, TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.ExternalLinked, result.Error?.Code);
        Assert.Equal("Error.ExternalLinkedElsewhere(GitHub)", result.Error?.Detail);
        store.DidNotReceiveWithAnyArgs().AddLogin(default!);
    }

    [Fact]
    public async Task AnAccountLinksOneOfEachProvider()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([Login(user.Id, ExternalProviders.GitHub, "7", "other")]);

        var result = await CreateService().LinkExternalAsync(user.Id, Octocat, TestSupport.Ct);

        Assert.Equal("Error.ExternalProviderTaken(GitHub)", result.Error?.Detail);
        store.DidNotReceiveWithAnyArgs().AddLogin(default!);
    }

    [Fact]
    public async Task LinkingWhatIsLinkedAlreadyDoesNothing()
    {
        var user = TestData.User();
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>()).Returns(Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat"));

        var result = await CreateService().LinkExternalAsync(user.Id, Octocat, TestSupport.Ct);

        Assert.True(result.IsSuccess);
        store.DidNotReceiveWithAnyArgs().AddLogin(default!);
    }

    [Fact]
    public async Task UnlinkingRemovesTheProvider()
    {
        var user = TestData.User();
        var login = Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([login]);

        var result = await CreateService().UnlinkExternalAsync(user.Id, "github", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        store.Received(1).RemoveLogin(login);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task TheLastWayToSignInStaysUntilThereIsAPassword()
    {
        var user = TestData.User();
        user.PasswordHash = null;
        var github = Login(user.Id, ExternalProviders.GitHub, "42", "octo-cat");
        store.FindUserAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([github]);

        var alone = await CreateService().UnlinkExternalAsync(user.Id, "github", TestSupport.Ct);
        store.ListLoginsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([Login(user.Id, ExternalProviders.Google, "g", "tom@example.com"), github]);
        var withGoogle = await CreateService().UnlinkExternalAsync(user.Id, "github", TestSupport.Ct);

        Assert.Equal(AccountErrorCodes.LastSignInMethod, alone.Error?.Code);
        Assert.True(withGoogle.IsSuccess);
        store.Received(1).RemoveLogin(github);
    }
}
