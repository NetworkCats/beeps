using System.Net;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class SecurityHeadersTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Theory]
    [InlineData("/public", HttpStatusCode.OK)]
    // Antiforgery sets its cookie here, and would let the site frame the page.
    [InlineData("/login", HttpStatusCode.OK)]
    [InlineData("/page-data/public", HttpStatusCode.OK)]
    [InlineData("/skill.md", HttpStatusCode.OK)]
    [InlineData("/skill/reference.md", HttpStatusCode.OK)]
    [InlineData("/images/logo.png", HttpStatusCode.OK)]
    [InlineData("/nope", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/nope", HttpStatusCode.NotFound)]
    public async Task EveryAnswerCarriesThem(string url, HttpStatusCode status)
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);

        Assert.Equal(status, response.StatusCode);
        AssertHeaders(response);
    }

    [Fact]
    public async Task SignedInPagesCarryThemToo()
    {
        var user = WebAuth.ArrangeUser(factory, handle: "frames");
        using var client = await WebAuth.SignInAsync(factory, user);

        using var shell = await client.GetAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        using var data = await Islands.GetAsync(client, "/settings");

        AssertHeaders(shell);
        AssertHeaders(data);
    }

    private static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("same-origin", Assert.Single(response.Headers.GetValues("Cross-Origin-Opener-Policy")));
    }
}
