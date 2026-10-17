using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class AboutPageTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task TheSignedOutHomePageLeadsToTheAboutPage()
    {
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        using var client = WebAuth.NewClient(factory);

        var home = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        var about = await client.GetStringAsync(new Uri("/about", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"aside-link\" href=\"/about\">Learn more</a>", home, StringComparison.Ordinal);
        Assert.Contains("<title>About beeps · beeps</title>", about, StringComparison.Ordinal);
        Assert.Contains("href=\"/skill.md\"", about, StringComparison.Ordinal);
        // The side column's about card is on every other page, not this one.
        Assert.Contains("id=\"about-title\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"about-title\"", about, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(about);
    }

    [Fact]
    public async Task TheAboutPageIsInChineseToo()
    {
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var about = await client.GetStringAsync(new Uri("/about", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<h1 class=\"column-title\">关于 beeps</h1>", about, StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();
}
