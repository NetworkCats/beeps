using System.Net;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Beeps.Tests.Pages;

public sealed class CulturePageTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task SwitchingRemembersTheLanguageAndGoesBack()
    {
        using var client = NewClient();

        using var response = await SwitchAsync(client, "zh-Hans", "/some/page?x=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/some/page?x=1", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("beeps.lang=", StringComparison.Ordinal));
        Assert.Contains("c%3Dzh-Hans%7Cuic%3Dzh-Hans", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("zh-TW,zh;q=0.9,en;q=0.8", "zh-Hans")]
    [InlineData("zh-HK", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-CN,en;q=0.5", "zh-Hans")]
    [InlineData("en-US,zh-TW;q=0.5", "en")]
    [InlineData("fr-FR", "en")]
    public async Task AnyChineseFirstGetsSimplifiedChinese(string acceptLanguage, string expected)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/public");
        request.Headers.Add("Accept-Language", acceptLanguage);

        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Contains($"<html lang=\"{expected}\"", await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalReturnUrlsAreIgnored()
    {
        using var client = NewClient();

        using var response = await SwitchAsync(client, "en", "https://evil.example/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OnAPageNotFoundTheSwitchesComeBackToItsAddress()
    {
        using var client = NewClient();

        using var response = await client.GetAsync(new Uri("/no/such/page?x=1", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var options = Islands.Props(await response.Content.ReadAsStringAsync(TestSupport.Ct), "site-options");
        Assert.Equal("/no/such/page?x=1", options.GetProperty("returnUrl").GetString());
    }

    [Fact]
    public async Task UnsupportedCulturesAreNotStored()
    {
        using var client = NewClient();

        using var response = await SwitchAsync(client, "fr", "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("beeps.lang=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PostingWithoutAntiforgeryTokenIsRejected()
    {
        using var client = NewClient();
        using var form = new FormUrlEncodedContent([KeyValuePair.Create("culture", "zh-Hans")]);

        using var response = await client.PostAsync(new Uri("/culture", UriKind.Relative), form, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignedOutPagesSwitchWithAPlainGetForm()
    {
        using var client = NewClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        // Public pages are output-cached, so they must not carry a per-visitor antiforgery token.
        Assert.Matches("<form class=\"footer-switch\" method=\"get\"[^>]* action=\"/culture\">", html);
        Assert.DoesNotContain("__RequestVerificationToken", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGetCannotChangeASignedInAccountsLanguage()
    {
        var user = WebAuth.ArrangeUser(factory, handle: "getter");
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await SwitchAsync(client, "zh-Hans", "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("en", user.Locale);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("beeps.lang=", StringComparison.Ordinal)));
    }

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // What the footer's form sends for a signed-out visitor.
    private static Task<HttpResponseMessage> SwitchAsync(HttpClient client, string culture, string returnUrl) =>
        client.GetAsync(new Uri($"/culture?culture={Uri.EscapeDataString(culture)}&returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), TestSupport.Ct);
}
