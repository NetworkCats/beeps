using System.Net;
using Beeps.Features.Accounts;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class AccountPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task RegisterShowsTurnstileThroughItsIsland()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/register", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("class=\"cf-turnstile captcha\" data-sitekey=\"test-site-key\"", html, StringComparison.Ordinal);
        // The app loads Turnstile's script for the widget.
        Assert.Equal("test-site-key", Islands.Props(html, "captcha").GetProperty("siteKey").GetString());
        // Not sent until the widget has passed, which the island says.
        Assert.Contains("type=\"submit\" disabled>", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/forgot-password")]
    public async Task TheWayInPagesOfferNoOtherWayIn(string path)
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct);
        var elsewhere = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        // The top bar's buttons and the side column's sign-up card: elsewhere, not here.
        foreach (var offer in new[] { "class=\"topbar-actions\"", "id=\"join-title\"" })
        {
            Assert.DoesNotContain(offer, html, StringComparison.Ordinal);
            Assert.Contains(offer, elsewhere, StringComparison.Ordinal);
        }

        // The rail offers signing in only: signing up is in the top bar and the side column.
        Assert.DoesNotContain("sidenav-cta", elsewhere, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisteringSignsInAndContinuesToVerification()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await WebAuth.PostFormAsync(client, "/register", "/register",
            ("Email", "tom@example.com"), ("Username", "tom"), ("Password", WebAuth.Password), ("cf-turnstile-response", "token"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/onboarding/verify", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.session=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        factory.Accounts.Received(1).AddUser(Arg.Is<User>(u => u.Actor.Handle == "tom"));
    }

    [Fact]
    public async Task FieldErrorsAppearNextToTheirInputs()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await WebAuth.PostFormAsync(client, "/register", "/register",
            ("Email", "nope"), ("Username", "tom"), ("Password", WebAuth.Password), ("cf-turnstile-response", "token"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("<p class=\"error\" id=\"Email-error\">Enter a valid email address.</p>", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"Username-error\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFieldsErrorIsDescribedAfterItsHelp()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await WebAuth.PostFormAsync(client, "/register", "/register",
            ("Email", "tom@example.com"), ("Username", "x"), ("Password", WebAuth.Password), ("cf-turnstile-response", "token"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Contains("id=\"Username-error\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"Username-help Username-error\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"Password-help\"", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"help\" id=\"Password-help\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosedRegistrationHidesTheForm()
    {
        factory.Accounts.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.Closed);
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/register", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("Sign-ups are closed at the moment.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Password\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InviteOnlyAsksForTheCode()
    {
        factory.Accounts.GetRegistrationModeAsync(Arg.Any<CancellationToken>()).Returns(RegistrationMode.InviteOnly);
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/register", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("name=\"InviteCode\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnverifiedAccountsAreSentToVerification()
    {
        var user = WebAuth.ArrangeUser(factory, verified: false);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);
        using var verify = await client.GetAsync(new Uri("/onboarding/verify", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/onboarding/verify", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task TheVerificationPageSaysWhereTheCodeWentAndTakesAnotherAddress()
    {
        var user = WebAuth.ArrangeUser(factory, verified: false);
        using var client = await WebAuth.SignInAsync(factory, user);

        var before = Islands.Props(await Islands.PageAsync(client, "/onboarding/verify"), "verify");
        using var changed = await WebAuth.PostFormAsync(client, "/onboarding/verify", "/onboarding/verify?handler=Email",
            ("NewEmail", "tom@example.org"), ("CurrentPassword", WebAuth.Password));
        var after = await Islands.PageAsync(client, "/onboarding/verify");

        Assert.Equal("tom@example.com", before.GetProperty("email").GetString());
        Assert.Equal("/onboarding/verify", changed.Headers.Location?.OriginalString);
        Assert.Equal("tom@example.org", Islands.Props(after, "verify").GetProperty("email").GetString());
        Assert.Contains("A new code is on its way.", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongPasswordKeepsTheOtherAddressFormOpen()
    {
        var user = WebAuth.ArrangeUser(factory, verified: false);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/onboarding/verify", "/onboarding/verify?handler=Email",
            ("NewEmail", "tom@example.org"), ("CurrentPassword", "not it"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(Islands.Props(html, "verify").GetProperty("changing").GetBoolean());
        Assert.Equal("tom@example.com", user.NormalizedEmail);
    }

    [Fact]
    public async Task LoginReturnsToTheRequestedPage()
    {
        WebAuth.ArrangeUser(factory);
        using var client = WebAuth.NewClient(factory);

        using var response = await WebAuth.PostFormAsync(client, "/login?ReturnUrl=%2Fagents", "/login",
            ("Login", "tom"), ("Password", WebAuth.Password), ("ReturnUrl", "/agents"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/agents", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ProtectedPagesAskAnonymousVisitorsToLogIn()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/login?ReturnUrl=%2Fagents", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task SigningInCreatesAServerSideSession()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TestBrowser/1.0");

        using var response = await WebAuth.PostFormAsync(client, "/login", "/login", ("Login", "tom"), ("Password", WebAuth.Password));

        var session = Assert.Single(factory.Sessions.All);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal("TestBrowser/1.0", session.UserAgent);
        // The cookie holds a protected session reference, not the account's claims.
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("beeps.session=", StringComparison.Ordinal));
        Assert.True(cookie.Split(';')[0].Length < 400, cookie);
    }

    [Fact]
    public async Task SigningOutRevokesTheSessionEverywhere()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var logout = await WebAuth.PostFormAsync(client, "/agents", "/logout");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.NotNull(Assert.Single(factory.Sessions.All).RevokedAt);
    }

    [Fact]
    public async Task ARevokedSessionCannotBeReplayed()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);
        using var before = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        // What a password reset or a ban does on the server, while the browser keeps its cookie.
        await factory.Sessions.RevokeAllAsync(user.Id, TestData.Now, TestSupport.Ct);
        using var after = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Fact]
    public async Task ABanEndsTheSession()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        user.BannedAt = TestData.Now;
        using var response = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task TheSavedLanguageBeatsTheBrowser()
    {
        var user = WebAuth.ArrangeUser(factory, locale: "zh-Hans");
        using var client = await WebAuth.SignInAsync(factory, user);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");

        var html = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/agents");

        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
        Assert.Equal("zh-Hans", Islands.Data(page).GetProperty("lang").GetString());
        Assert.Contains("我的 agent", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchingLanguageWhileSignedInSavesIt()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", "/culture", ("culture", "zh-Hans"), ("returnUrl", "/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("zh-Hans", user.Locale);
    }

    [Fact]
    public async Task ForgotPasswordCarriesTheAddressWithoutPuttingItInTheUrl()
    {
        using var client = factory.CreateClient();

        using var response = await WebAuth.PostFormAsync(client, "/forgot-password", "/forgot-password",
            ("Email", "tom@example.com"), ("cf-turnstile-response", "token"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal("/reset-password", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains("value=\"tom@example.com\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/login", false)]
    [InlineData("/register", false)]
    [InlineData("/forgot-password", true)]
    [InlineData("/reset-password", true)]
    public async Task SignedInOnlyTheWayBackInStaysOpen(string path, bool open)
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestSupport.Ct);

        // Settings links a forgotten password from beside the current one; signing in or up again has no use.
        Assert.Equal(open ? HttpStatusCode.OK : HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(open ? null : "/", response.Headers.Location?.OriginalString);
    }

    public void Dispose() => factory.Dispose();
}
