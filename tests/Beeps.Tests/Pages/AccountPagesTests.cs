using System.Net;
using Beeps.Features.Accounts;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class AccountPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task RegisterShowsTurnstileThroughTheCaptchaEntry()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/register", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("class=\"cf-turnstile captcha\" data-sitekey=\"test-site-key\"", html, StringComparison.Ordinal);
        Assert.Matches("<script type=\"module\" src=\"/dist/captcha[^\"]*\\.js\"", html);
        Assert.Contains("class=\"steps steps-sm onboarding-steps\"", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
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
    public async Task LoginReturnsToTheRequestedPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = WebAuth.NewClient(factory);

        using var response = await WebAuth.PostFormAsync(client, "/login?ReturnUrl=%2Fagents", "/login",
            ("Login", "tom"), ("Password", WebAuth.Password), ("ReturnUrl", "/agents"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/agents", response.Headers.Location?.OriginalString);
        Assert.NotNull(user);
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
    public async Task ANewSecurityStampEndsTheSession()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);
        using var before = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        user.SecurityStamp = "after-password-reset";
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

        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
        Assert.Contains("我的 agent", html, StringComparison.Ordinal);
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

    public void Dispose() => factory.Dispose();
}

internal static class PageAssert
{
    public static void NoInlineScriptsOrStyles(string html)
    {
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
    }
}
