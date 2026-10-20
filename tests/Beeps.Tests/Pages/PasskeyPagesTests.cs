using System.Net;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

/// <summary>
/// Passkeys from end to end (PLAN 6.14): added in the settings, then signing in with one on the login page, or after
/// the password as the second step. The ceremonies are the real ones, answered by an authenticator in software; only
/// where the passkeys are kept is the mocked account store.
/// </summary>
public sealed class PasskeyPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();
    private readonly TestAuthenticator authenticator = new();
    private readonly List<Passkey> passkeys = [];
    private readonly User tom;

    public PasskeyPagesTests()
    {
        tom = WebAuth.ArrangeUser(factory);
        factory.Accounts.When(a => a.AddPasskey(Arg.Any<Passkey>())).Do(ci => passkeys.Add(ci.Arg<Passkey>()));
        factory.Accounts.When(a => a.RemovePasskey(Arg.Any<Passkey>())).Do(ci => passkeys.Remove(ci.Arg<Passkey>()));
        factory.Accounts.ListPasskeysAsync(default, default).ReturnsForAnyArgs(ci => (IReadOnlyList<Passkey>)[.. passkeys.Where(p => p.UserId == ci.Arg<Guid>())]);
        factory.Accounts.FindPasskeyAsync(default!, default).ReturnsForAnyArgs(ci => passkeys.FirstOrDefault(p => p.CredentialId.AsSpan().SequenceEqual(ci.Arg<byte[]>())));
    }

    [Fact]
    public async Task TheLoginPageOffersAPasskey()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("data-island=\"passkey-sign-in\"", html, StringComparison.Ordinal);
        Assert.Contains("Sign in with a passkey", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task APasskeyAddedInTheSettingsSignsInWithoutAPassword()
    {
        using (var signedIn = await WebAuth.SignInAsync(factory, tom))
        {
            signedIn.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15");
            using var added = await AddPasskeyAsync(signedIn);

            Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
            Assert.Equal("/settings/passkeys", added.Headers.Location?.OriginalString);
        }

        var passkey = Assert.Single(passkeys);
        Assert.Equal(tom.Id, passkey.UserId);
        Assert.Equal(authenticator.Keys[0].Id, passkey.CredentialId);
        Assert.Contains("Macintosh", passkey.UserAgent, StringComparison.Ordinal);

        using var client = WebAuth.NewClient(factory);
        var (options, ticket) = await OptionsAsync(client, "/login", "/login?handler=PasskeyOptions");
        using var signIn = await WebAuth.PostFormAsync(client, "/login", "/login?handler=Passkey",
            ("Credential", authenticator.Get(options)), ("Ticket", ticket), ("ReturnUrl", "/notifications"));

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/notifications", signIn.Headers.Location?.OriginalString);
        Assert.NotNull(passkey.LastUsedAt);
        Assert.Equal(1, passkey.SignCount);
        await AssertSignedInAsync(client);
    }

    [Fact]
    public async Task APasskeyThatDidNotHoldUpSignsNobodyIn()
    {
        using (var signedIn = await WebAuth.SignInAsync(factory, tom))
        {
            using var added = await AddPasskeyAsync(signedIn);
        }

        using var client = WebAuth.NewClient(factory);
        var (options, ticket) = await OptionsAsync(client, "/login", "/login?handler=PasskeyOptions");
        authenticator.Origin = "https://beeps.example";
        using var signIn = await WebAuth.PostFormAsync(client, "/login", "/login?handler=Passkey", ("Credential", authenticator.Get(options)), ("Ticket", ticket));

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Contains("The passkey couldn&#x27;t be verified.", await signIn.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        await AssertSignedOutAsync(client);
    }

    [Fact]
    public async Task WithTwoStepSignInThePasswordLeadsOnToAPasskey()
    {
        using (var signedIn = await WebAuth.SignInAsync(factory, tom))
        {
            using var added = await AddPasskeyAsync(signedIn);
            using var on = await WebAuth.PostFormAsync(signedIn, "/settings/passkeys", "/settings/passkeys?handler=PasskeyTwoFactor", ("TwoFactor", "true"), ("CurrentPassword", WebAuth.Password));
            Assert.Equal(HttpStatusCode.Redirect, on.StatusCode);
        }

        Assert.True(tom.PasskeyTwoFactor);
        using var client = WebAuth.NewClient(factory);
        using var password = await WebAuth.PostFormAsync(client, "/login", "/login", ("Login", "tom"), ("Password", WebAuth.Password), ("ReturnUrl", "/notifications"));

        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
        Assert.Equal("/login/passkey?returnUrl=%2Fnotifications", password.Headers.Location?.OriginalString);
        await AssertSignedOutAsync(client);

        var page = await client.GetStringAsync(password.Headers.Location, TestSupport.Ct);
        Assert.Contains("Verify with a passkey", page, StringComparison.Ordinal);
        var (options, ticket) = await OptionsAsync(client, "/login/passkey", "/login/passkey?handler=PasskeyOptions");
        Assert.Single(JsonDocument.Parse(options).RootElement.GetProperty("allowCredentials").EnumerateArray());
        using var signIn = await WebAuth.PostFormAsync(client, "/login/passkey", "/login/passkey?handler=Passkey",
            ("Credential", authenticator.Get(options)), ("Ticket", ticket), ("ReturnUrl", "/notifications"));

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/notifications", signIn.Headers.Location?.OriginalString);
        await AssertSignedInAsync(client);
        // Done with: the step is not there to take again.
        using var again = await client.GetAsync(new Uri("/login/passkey", UriKind.Relative), TestSupport.Ct);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Fact]
    public async Task WithTwoStepSignInAPasskeyAloneSignsNobodyIn()
    {
        using (var signedIn = await WebAuth.SignInAsync(factory, tom))
        {
            using var added = await AddPasskeyAsync(signedIn);
        }

        tom.PasskeyTwoFactor = true;
        using var client = WebAuth.NewClient(factory);
        var (options, ticket) = await OptionsAsync(client, "/login", "/login?handler=PasskeyOptions");
        using var signIn = await WebAuth.PostFormAsync(client, "/login", "/login?handler=Passkey", ("Credential", authenticator.Get(options)), ("Ticket", ticket));

        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
        Assert.Contains("This account uses its passkeys as a second step.", await signIn.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        await AssertSignedOutAsync(client);
    }

    [Fact]
    public async Task TheSecondStepIsOnlyThereAfterTheFirst()
    {
        using var client = WebAuth.NewClient(factory);

        using var page = await client.GetAsync(new Uri("/login/passkey?returnUrl=%2Fnotifications", UriKind.Relative), TestSupport.Ct);
        using var options = await WebAuth.PostFormAsync(client, "/login", "/login/passkey?handler=PasskeyOptions");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/login?ReturnUrl=%2Fnotifications", page.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, options.StatusCode);
        Assert.Equal("Signing in took too long. Please start again.", JsonDocument.Parse(await options.Content.ReadAsStringAsync(TestSupport.Ct)).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task APasskeyRemovedInTheSettingsNoLongerSignsIn()
    {
        using (var signedIn = await WebAuth.SignInAsync(factory, tom))
        {
            using var added = await AddPasskeyAsync(signedIn);
            using var removed = await WebAuth.PostFormAsync(signedIn, "/settings/passkeys", "/settings/passkeys?handler=RemovePasskey", ("PasskeyId", passkeys[0].Id.ToString()));
            Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        }

        Assert.Empty(passkeys);
        using var client = WebAuth.NewClient(factory);
        var (options, ticket) = await OptionsAsync(client, "/login", "/login?handler=PasskeyOptions");
        using var signIn = await WebAuth.PostFormAsync(client, "/login", "/login?handler=Passkey", ("Credential", authenticator.Get(options)), ("Ticket", ticket));

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        await AssertSignedOutAsync(client);
    }

    [Fact]
    public async Task AddingAPasskeyNeedsAMember()
    {
        using var client = WebAuth.NewClient(factory);

        using var options = await client.PostAsync(new Uri("/settings/passkeys?handler=PasskeyOptions", UriKind.Relative), new FormUrlEncodedContent([]), TestSupport.Ct);

        Assert.NotEqual(HttpStatusCode.OK, options.StatusCode);
        factory.Accounts.DidNotReceiveWithAnyArgs().AddPasskey(default!);
    }

    public void Dispose()
    {
        authenticator.Dispose();
        factory.Dispose();
    }

    /// <summary>Adds a passkey as the settings page does: the options, the authenticator, then what it made.</summary>
    private async Task<HttpResponseMessage> AddPasskeyAsync(HttpClient client)
    {
        var (options, ticket) = await OptionsAsync(client, "/settings/passkeys", "/settings/passkeys?handler=PasskeyOptions");
        return await WebAuth.PostFormAsync(client, "/settings/passkeys", "/settings/passkeys?handler=AddPasskey", ("Credential", authenticator.Create(options)), ("Ticket", ticket));
    }

    /// <summary>What a passkey button asks for first: the options for the browser, and the ticket to send back.</summary>
    private static async Task<(string Options, string Ticket)> OptionsAsync(HttpClient client, string pageUrl, string url)
    {
        using var response = await WebAuth.PostFormAsync(client, pageUrl, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        return (answer.RootElement.GetProperty("options").GetRawText(), answer.RootElement.GetProperty("ticket").GetString()!);
    }

    private static async Task AssertSignedInAsync(HttpClient client)
    {
        using var settings = await client.GetAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
    }

    private static async Task AssertSignedOutAsync(HttpClient client)
    {
        using var settings = await client.GetAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        Assert.Equal(HttpStatusCode.Redirect, settings.StatusCode);
    }
}
