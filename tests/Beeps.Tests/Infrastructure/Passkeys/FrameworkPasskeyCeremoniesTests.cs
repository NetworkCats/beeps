using System.Buffers.Text;
using System.Text.Json.Nodes;
using Beeps.Features.Accounts;
using Beeps.Features.Site;
using Beeps.Infrastructure.Passkeys;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Passkeys;

/// <summary>
/// The WebAuthn ceremonies, run through the framework's passkey handler with an authenticator in software
/// (<see cref="TestAuthenticator"/>): what it makes and signs holds up, and what is wrong in any part of it does not.
/// </summary>
public sealed class FrameworkPasskeyCeremoniesTests : IDisposable
{
    private static readonly Guid Tom = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly Guid Alice = Guid.Parse("018f0000-0000-7000-8000-000000000002");

    private readonly FrameworkPasskeyCeremonies ceremonies = new(
        Options.Create(new SiteOptions { BaseUrl = "https://beeps.test" }),
        NullLogger<FrameworkPasskeyCeremonies>.Instance);

    private readonly TestAuthenticator authenticator = new();

    [Fact]
    public async Task TheOptionsAskForAPasskeyTheBrowserCanOfferByItself()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);

        var options = JsonNode.Parse(challenge.OptionsJson)!;
        Assert.Equal("beeps.test", options["rp"]!["id"]!.GetValue<string>());
        Assert.Equal(Tom.ToString("D"), System.Text.Encoding.UTF8.GetString(Base64Url.DecodeFromChars(options["user"]!["id"]!.GetValue<string>())));
        Assert.Equal("tom", options["user"]!["name"]!.GetValue<string>());
        Assert.Equal("Tom", options["user"]!["displayName"]!.GetValue<string>());
        Assert.Equal("required", options["authenticatorSelection"]!["residentKey"]!.GetValue<string>());
        Assert.Equal("required", options["authenticatorSelection"]!["userVerification"]!.GetValue<string>());
        Assert.Contains(options["pubKeyCredParams"]!.AsArray(), p => p!["alg"]!.GetValue<int>() == -7);
        Assert.NotEmpty(challenge.State);
    }

    [Fact]
    public async Task APasskeyMadeForTheOptionsHoldsUp()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);

        var created = await ceremonies.FinishRegistrationAsync(authenticator.Create(challenge.OptionsJson), challenge.State, TestSupport.Ct);

        Assert.NotNull(created);
        Assert.Equal(authenticator.Keys[0].Id, created.CredentialId);
        Assert.NotEmpty(created.PublicKey);
        Assert.Equal(0u, created.SignCount);
        Assert.Equal(["internal", "hybrid"], created.Transports ?? []);
        Assert.True(created.BackupEligible);
        Assert.True(created.BackedUp);
    }

    [Fact]
    public async Task APasswordManagersAnswerWithoutExtensionResultsHoldsUp()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);
        var made = WithoutExtensionResults(authenticator.Create(challenge.OptionsJson));
        var created = await ceremonies.FinishRegistrationAsync(made, challenge.State, TestSupport.Ct);
        Assert.NotNull(created);
        var passkey = Stored(Tom, created.CredentialId, created.PublicKey);

        var signIn = await ceremonies.BeginAssertionAsync(null, []);
        var signed = WithoutExtensionResults(authenticator.Get(signIn.OptionsJson));

        Assert.NotNull(await ceremonies.FinishAssertionAsync(signed, signIn.State, passkey, TestSupport.Ct));
    }

    [Fact]
    public async Task TheAccountsOwnPasskeysAreNotMadeAgain()
    {
        var existing = Stored(Tom, new byte[] { 1, 2, 3 });

        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", [existing]);

        var excluded = JsonNode.Parse(challenge.OptionsJson)!["excludeCredentials"]!.AsArray();
        Assert.Equal(Base64Url.EncodeToString(existing.CredentialId), Assert.Single(excluded)!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task APasskeyMadeOnAnotherSiteIsRefused()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);
        authenticator.Origin = "https://beeps.example";

        Assert.Null(await ceremonies.FinishRegistrationAsync(authenticator.Create(challenge.OptionsJson), challenge.State, TestSupport.Ct));
    }

    [Fact]
    public async Task APasskeyMadeForOtherOptionsIsRefused()
    {
        var first = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);
        var second = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);

        Assert.Null(await ceremonies.FinishRegistrationAsync(authenticator.Create(first.OptionsJson), second.State, TestSupport.Ct));
    }

    [Fact]
    public async Task APasskeyThatDidNotCheckWhoHeldItIsRefused()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);
        authenticator.VerifiesUser = false;

        Assert.Null(await ceremonies.FinishRegistrationAsync(authenticator.Create(challenge.OptionsJson), challenge.State, TestSupport.Ct));
    }

    [Fact]
    public async Task AnswersThatAreNoCredentialAreRefused()
    {
        var challenge = await ceremonies.BeginRegistrationAsync(Tom, "tom", "Tom", []);

        Assert.Null(await ceremonies.FinishRegistrationAsync("{}", challenge.State, TestSupport.Ct));
        Assert.Null(await ceremonies.FinishRegistrationAsync("not json", challenge.State, TestSupport.Ct));
        Assert.Null(ceremonies.CredentialIdOf("not json"));
        Assert.Null(ceremonies.CredentialIdOf("""{"id":42}"""));
        Assert.Null(ceremonies.CredentialIdOf("""{"id":"***"}"""));
    }

    [Fact]
    public async Task SigningInWithAnyPasskeyHoldsUpForThePasskeyNamed()
    {
        var passkey = await RegisterAsync(Tom);
        var challenge = await ceremonies.BeginAssertionAsync(null, []);
        var options = JsonNode.Parse(challenge.OptionsJson)!;

        var credential = authenticator.Get(challenge.OptionsJson);
        var use = await ceremonies.FinishAssertionAsync(credential, challenge.State, passkey, TestSupport.Ct);

        Assert.Equal("beeps.test", options["rpId"]!.GetValue<string>());
        Assert.Empty(options["allowCredentials"]?.AsArray() ?? []);
        Assert.Equal(passkey.CredentialId, ceremonies.CredentialIdOf(credential));
        Assert.NotNull(use);
        Assert.Equal(1u, use.SignCount);
        Assert.True(use.BackedUp);
    }

    [Fact]
    public async Task ASignatureByAnotherKeyIsRefused()
    {
        var passkey = await RegisterAsync(Tom);
        await RegisterAsync(Tom);
        var challenge = await ceremonies.BeginAssertionAsync(null, []);

        // Signed by the second passkey, checked against the first one's key.
        var credential = authenticator.Get(challenge.OptionsJson, authenticator.Keys[1]);

        Assert.Null(await ceremonies.FinishAssertionAsync(credential, challenge.State, passkey, TestSupport.Ct));
    }

    [Fact]
    public async Task APasskeySayingItIsSomeoneElsesIsRefused()
    {
        var passkey = await RegisterAsync(Tom);
        var challenge = await ceremonies.BeginAssertionAsync(null, []);
        var credential = authenticator.Get(challenge.OptionsJson);

        // The same key, recorded as Alice's: the user handle the authenticator gave is Tom's.
        var alices = Stored(Alice, passkey.CredentialId, passkey.PublicKey);

        Assert.Null(await ceremonies.FinishAssertionAsync(credential, challenge.State, alices, TestSupport.Ct));
    }

    [Fact]
    public async Task ASignatureForOtherOptionsIsRefused()
    {
        var passkey = await RegisterAsync(Tom);
        var first = await ceremonies.BeginAssertionAsync(null, []);
        var second = await ceremonies.BeginAssertionAsync(null, []);

        Assert.Null(await ceremonies.FinishAssertionAsync(authenticator.Get(first.OptionsJson), second.State, passkey, TestSupport.Ct));
    }

    [Fact]
    public async Task ASignCountThatWentBackIsRefused()
    {
        var passkey = await RegisterAsync(Tom);
        passkey.SignCount = 5;
        var challenge = await ceremonies.BeginAssertionAsync(null, []);

        Assert.Null(await ceremonies.FinishAssertionAsync(authenticator.Get(challenge.OptionsJson), challenge.State, passkey, TestSupport.Ct));
    }

    [Fact]
    public async Task TheSecondStepAsksForTheAccountsOwnPasskeys()
    {
        var passkey = await RegisterAsync(Tom);

        var challenge = await ceremonies.BeginAssertionAsync(Tom, [passkey]);
        var use = await ceremonies.FinishAssertionAsync(authenticator.Get(challenge.OptionsJson), challenge.State, passkey, TestSupport.Ct);

        var allowed = JsonNode.Parse(challenge.OptionsJson)!["allowCredentials"]!.AsArray();
        Assert.Equal(Base64Url.EncodeToString(passkey.CredentialId), Assert.Single(allowed)!["id"]!.GetValue<string>());
        Assert.NotNull(use);
    }

    [Fact]
    public async Task TheSecondStepForOneAccountRefusesAnothersPasskey()
    {
        var alices = await RegisterAsync(Alice);

        var challenge = await ceremonies.BeginAssertionAsync(Tom, []);

        Assert.Null(await ceremonies.FinishAssertionAsync(authenticator.Get(challenge.OptionsJson, authenticator.Keys[0]), challenge.State, alices, TestSupport.Ct));
    }

    public void Dispose() => authenticator.Dispose();

    private static string WithoutExtensionResults(string credentialJson)
    {
        var credential = JsonNode.Parse(credentialJson)!.AsObject();
        credential.Remove("clientExtensionResults");
        return credential.ToJsonString();
    }

    /// <summary>A passkey made by the authenticator for the account, as it would be kept.</summary>
    private async Task<Passkey> RegisterAsync(Guid userId)
    {
        var challenge = await ceremonies.BeginRegistrationAsync(userId, "someone", "Someone", []);
        var created = await ceremonies.FinishRegistrationAsync(authenticator.Create(challenge.OptionsJson), challenge.State, TestSupport.Ct);
        Assert.NotNull(created);
        return Stored(userId, created.CredentialId, created.PublicKey, created.BackupEligible);
    }

    private static Passkey Stored(Guid userId, byte[] credentialId, byte[]? publicKey = null, bool backupEligible = true) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        CredentialId = credentialId,
        PublicKey = publicKey ?? [],
        BackupEligible = backupEligible,
        BackedUp = backupEligible,
        CreatedAt = TestData.Now,
    };
}
