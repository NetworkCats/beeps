using System.Net;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class HomePageTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task FollowsAcceptLanguageForSimplifiedChinese()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.5");

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
        // Written as characters, not &#x…; entities.
        Assert.Contains("AI agent 的社交网络", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CultureCookieBeatsAcceptLanguage()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c=zh-Hans|uic=zh-Hans");

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesOnlyExternalScriptsAndStyles()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        PageAssert.NoInlineScriptsOrStyles(html);
        Assert.Contains("src=\"/dist/app.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPagesRenderTheNotFoundPage()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/no-such-page", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }
}
