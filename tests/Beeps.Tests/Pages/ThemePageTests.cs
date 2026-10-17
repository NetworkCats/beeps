using System.Net;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class ThemePageTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    public ThemePageTests() =>
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));

    [Fact]
    public async Task PagesFollowTheSystemUntilASchemeIsPicked()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"theme-color\" content=\"#0a0a0a\" data-scheme=\"dark\" media=\"(prefers-color-scheme: dark)\">", html, StringComparison.Ordinal);
        // Public pages are output-cached: a GET form, with no antiforgery token.
        Assert.Matches("<form class=\"footer-switch\" method=\"get\" action=\"/theme\">", html);
        Assert.Contains("<option value=\"system\" selected=\"selected\">Auto</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APickedSchemeIsKeptAndDrawn()
    {
        using var client = WebAuth.NewClient(factory);
        // A cached copy drawn for the system's scheme must not be what the next page shows.
        await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        using var response = await client.GetAsync(new Uri("/theme?theme=dark&returnUrl=%2Fpublic", UriKind.Relative), TestSupport.Ct);
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/public", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.theme=dark", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"theme-color\" content=\"#0a0a0a\" data-scheme=\"dark\" media=\"all\">", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"dark\" selected=\"selected\">Dark</option>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowingTheSystemAgainForgetsTheScheme()
    {
        using var client = WebAuth.NewClient(factory);
        using var picked = await client.GetAsync(new Uri("/theme?theme=light", UriKind.Relative), TestSupport.Ct);

        using var purple = await client.GetAsync(new Uri("/theme?theme=purple&returnUrl=https%3A%2F%2Fevil.example", UriKind.Relative), TestSupport.Ct);
        var stillLight = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        using var system = await client.GetAsync(new Uri("/theme?theme=system", UriKind.Relative), TestSupport.Ct);
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/", purple.Headers.Location?.OriginalString);
        Assert.Contains("<html lang=\"en\" data-theme=\"light\">", stillLight, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignedInAccountKeepsItsSchemeOnEveryDevice()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/settings", "/theme", ("theme", "dark"), ("returnUrl", "/settings"));
        using var elsewhere = await WebAuth.SignInAsync(factory, user);
        var html = await elsewhere.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/settings", response.Headers.Location?.OriginalString);
        Assert.Equal("dark", user.Theme);
        // Another browser, with no cookie of its own.
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", html, StringComparison.Ordinal);
        Assert.Matches("<form class=\"footer-switch\" method=\"post\" action=\"/theme\">", html);
    }

    [Fact]
    public async Task TheAccountsSchemeBeatsTheBrowsers()
    {
        var user = WebAuth.ArrangeUser(factory);
        user.Theme = "system";
        using var client = await WebAuth.SignInAsync(factory, user);
        client.DefaultRequestHeaders.Add("Cookie", "beeps.theme=dark");

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGetCannotChangeASignedInAccountsScheme()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/theme?theme=dark", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(user.Theme);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("beeps.theme=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheSettingsPageSavesTheScheme()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        using var response = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Theme", ("Theme", "light"));

        Assert.Contains("<select class=\"select settings-select\" id=\"Theme\" name=\"Theme\">", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("light", user.Theme);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.theme=light", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NarrowScreensFindTheFooterInTheTopBarMenu()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        // The same links and settings twice: the "more" menu of the top bar, and the side column's footer.
        Assert.Contains("<details class=\"menu topbar-more\">", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("<a class=\"footer-link\" href=\"/about\">About</a>").Length - 1);
        Assert.Equal(2, html.Split("action=\"/theme\"").Length - 1);
    }

    public void Dispose() => factory.Dispose();
}
