using System.Net;
using System.Security.Claims;
using System.Web;
using Beeps.Features.Accounts;
using Beeps.Features.Auth;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Pages;

/// <summary>Signing in and up with Google and GitHub, and linking them from the settings (PLAN 6.14).</summary>
public sealed class ExternalLoginPagesTests : IDisposable
{
    // GitHub has an app here; Google has none, as on a developer's machine.
    private readonly BeepsWebApplicationFactory factory = new()
    {
        Settings = new Dictionary<string, string>
        {
            ["OAuth:GitHub:ClientId"] = "test-client",
            ["OAuth:GitHub:ClientSecret"] = "test-secret",
        },
    };

    [Theory]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/public")]
    public async Task TheWaysInOfferBothProviders(string path)
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct);

        // Links, not forms, so the page carries no token and can be cached for everyone.
        Assert.Contains("href=\"/oauth/google\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/oauth/github\"", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheLoginPageSendsWhereItWasToGoOnToTheProvider()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/login?ReturnUrl=%2Fnotifications", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("href=\"/oauth/github?returnUrl=%2Fnotifications\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProviderWithoutAnAppHereSaysSo()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/oauth/google", UriKind.Relative), TestSupport.Ct);
        var login = await client.GetStringAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        Assert.Contains("Signing in with Google isn&#x27;t set up on this site yet.", login, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProviderWithAnAppSendsTheBrowserThere()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/oauth/github", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://github.com/login/oauth/authorize", location.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("http://localhost/oauth/github/callback", query["redirect_uri"]);
        Assert.Equal("read:user user:email", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.NotEmpty(query["state"]!);
        // What ties the answer to this browser, named after beeps as every cookie is.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.correlation.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerThatDoesNotHoldUpGoesBackToTheLoginPage()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/oauth/github/callback?code=x&state=forged", UriKind.Relative), TestSupport.Ct);
        using var failed = await client.GetAsync(response.Headers.Location, TestSupport.Ct);
        var login = await client.GetStringAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/oauth/github?handler=Failed&link=false", response.Headers.Location?.OriginalString);
        Assert.Equal("/login", failed.Headers.Location?.OriginalString);
        Assert.Contains("Couldn&#x27;t sign in with GitHub. Please try again.", login, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingFromAProviderIsStartingAgain()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/oauth/complete", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ALinkedAccountIsSignedIn()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Accounts.FindLoginAsync(ExternalProviders.GitHub, "42", Arg.Any<CancellationToken>())
            .Returns(new ExternalLogin { UserId = user.Id, Provider = ExternalProviders.GitHub, ProviderKey = "42", Name = "octocat" });
        using var client = factory.Server.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/oauth/complete", Ticket(returnUrl: "/notifications"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/notifications", response.Headers.Location?.OriginalString);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("beeps.session=", StringComparison.Ordinal));
        // What the provider said is used up.
        Assert.Contains(cookies, c => c.StartsWith("beeps.external=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SomeoneNewPicksAUsernameAndIsIn()
    {
        using var client = factory.Server.CreateClient();
        var ticket = Ticket();

        using var form = await SendAsync(client, HttpMethod.Get, "/oauth/complete", ticket);
        var html = await form.Content.ReadAsStringAsync(TestSupport.Ct);
        var antiforgery = form.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("beeps.csrf=", StringComparison.Ordinal)).Split(';')[0];
        using var content = new FormUrlEncodedContent([
            KeyValuePair.Create("Username", "octo"),
            KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html))]);
        using var done = await SendAsync(client, HttpMethod.Post, "/oauth/complete", ticket, antiforgery, content);

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("You&#x27;re signing up with your GitHub account (octo@example.com).", html, StringComparison.Ordinal);
        Assert.Contains("value=\"octo_cat\"", html, StringComparison.Ordinal);
        // No password, no code, no Turnstile.
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cf-turnstile", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal("/onboarding/agent", done.Headers.Location?.OriginalString);
        Assert.Contains(done.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.session=", StringComparison.Ordinal));
        factory.Accounts.Received(1).AddUser(Arg.Is<User>(u => u.Actor.Handle == "octo" && u.EmailVerifiedAt != null && u.PasswordHash == null));
        factory.Accounts.Received(1).AddLogin(Arg.Is<ExternalLogin>(l => l.Provider == ExternalProviders.GitHub && l.ProviderKey == "42"));
    }

    [Fact]
    public async Task ALinkIsFinishedOnlyByTheAccountThatStartedIt()
    {
        var tom = WebAuth.ArrangeUser(factory);
        var session = await SessionCookieAsync(tom);
        using var client = factory.Server.CreateClient();

        using var mine = await SendAsync(client, HttpMethod.Get, "/oauth/complete", Ticket(linkFrom: tom.Id), session);
        using var someoneElses = await SendAsync(client, HttpMethod.Get, "/oauth/complete", Ticket(linkFrom: Guid.NewGuid()), session);

        Assert.Equal("/settings/account", mine.Headers.Location?.OriginalString);
        Assert.Equal("/settings/account", someoneElses.Headers.Location?.OriginalString);
        factory.Accounts.Received(1).AddLogin(Arg.Is<ExternalLogin>(l => l.UserId == tom.Id && l.ProviderKey == "42"));
    }

    [Fact]
    public async Task TheSettingsListTheProvidersAndUnlinkThem()
    {
        var tom = WebAuth.ArrangeUser(factory);
        var login = new ExternalLogin { UserId = tom.Id, Provider = ExternalProviders.GitHub, ProviderKey = "42", Name = "octocat" };
        factory.Accounts.ListLoginsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([login]);
        using var client = await WebAuth.SignInAsync(factory, tom);

        var data = await client.GetStringAsync(Islands.DataUrl("/settings/account"), TestSupport.Ct);
        using var response = await WebAuth.PostFormAsync(client, "/settings/account", "/settings/account?handler=Unlink", ("Provider", "github"));

        Assert.Contains("\"connections\":[{\"provider\":\"google\",\"label\":\"Google\"},{\"provider\":\"github\",\"label\":\"GitHub\",\"name\":\"octocat\"}]", data, StringComparison.Ordinal);
        Assert.Equal("/settings/account", response.Headers.Location?.OriginalString);
        factory.Accounts.Received(1).RemoveLogin(login);
    }

    public void Dispose() => factory.Dispose();

    /// <summary>What GitHub said, as the cookie keeps it until <c>/oauth/complete</c>.</summary>
    private string Ticket(string? returnUrl = null, Guid? linkFrom = null)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Email, "octo@example.com"),
                new Claim(ClaimTypes.Name, "The Octocat"),
                new Claim("beeps:username", "octo-cat"),
            ],
            ExternalProviders.GitHub);
        var properties = new AuthenticationProperties();
        if (returnUrl is not null)
        {
            properties.Items[ExternalAuth.ReturnItem] = returnUrl;
        }

        if (linkFrom is { } userId)
        {
            properties.Items[ExternalAuth.LinkItem] = userId.ToString("D");
        }

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSchemes.External);
        return "beeps.external=" + options.TicketDataFormat.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, AuthSchemes.External));
    }

    /// <summary>A request with exactly these cookies: the test server's own client keeps none.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string cookie, string? more = null, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Add("Cookie", more is null ? cookie : $"{cookie}; {more}");
        return await client.SendAsync(request, TestSupport.Ct);
    }

    /// <summary>A session cookie for the user, from signing in on a client that hands it over.</summary>
    private async Task<string> SessionCookieAsync(User user)
    {
        using var client = factory.Server.CreateClient();
        using var page = await client.GetAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        var antiforgery = page.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("beeps.csrf=", StringComparison.Ordinal)).Split(';')[0];
        using var content = new FormUrlEncodedContent([
            KeyValuePair.Create("Login", user.Actor.Handle!),
            KeyValuePair.Create("Password", WebAuth.Password),
            KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(await page.Content.ReadAsStringAsync(TestSupport.Ct)))]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/login", UriKind.Relative)) { Content = content };
        request.Headers.Add("Cookie", antiforgery);
        using var response = await client.SendAsync(request, TestSupport.Ct);
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("beeps.session=", StringComparison.Ordinal)).Split(';')[0];
    }
}
