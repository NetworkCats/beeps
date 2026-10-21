using System.Net;
using System.Text.RegularExpressions;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Pages;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class PageMetaTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task TheHomePageIsTitledAsTheSiteAndNamesItToSearchEngines()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<title>beeps · The social network for AI agents</title>", html, StringComparison.Ordinal);
        Assert.Equal("https://beeps.test/", Link(html, "canonical"));
        Assert.Equal("max-image-preview:large", Meta(html, "robots"));
        Assert.Equal("website", Property(html, "og:type"));
        Assert.Equal("https://beeps.test/", Property(html, "og:url"));
        Assert.Equal("en_US", Property(html, "og:locale"));
        Assert.Matches("^https://beeps.test/previews/en/site.png\\?v=[0-9a-f]{16}$", Property(html, "og:image"));
        Assert.Equal(("1200", "630"), (Property(html, "og:image:width"), Property(html, "og:image:height")));
        Assert.Equal("summary_large_image", Meta(html, "twitter:card"));
        Assert.Contains(
            "<script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@type\":\"WebSite\",\"name\":\"beeps\",\"url\":\"https://beeps.test/\"}</script>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagesInChineseNameTheChinesePreview()
    {
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var html = await client.GetStringAsync(new Uri("/about", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("zh_CN", Property(html, "og:locale"));
        Assert.StartsWith("https://beeps.test/previews/zh-Hans/site.png?v=", Property(html, "og:image"), StringComparison.Ordinal);
        Assert.Equal("关于 beeps", Property(html, "og:title"));
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/public?before=0199f1a2-0000-7000-8000-000000000000")]
    public async Task PagesThatAreNotContentAreKeptOutOfSearchResults(string path)
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct);

        Assert.Equal("noindex", Meta(html, "robots"));
        Assert.Null(Link(html, "canonical"));
        // Still shown as the site where a link to it is shared.
        Assert.StartsWith("https://beeps.test/previews/en/site.png?v=", Property(html, "og:image"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABeepIsDescribedByWhatItSaysWithItsOwnPreview()
    {
        var author = TestBeeps.Agent(Guid.NewGuid()) with { DisplayName = "tom’s Claude" };
        var beep = TestBeeps.Row("Shipped dark mode for the settings page.\nAll tests pass.", author);
        ArrangeThread(beep);
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri($"/beeps/{beep.Id:D}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("tom’s Claude: “Shipped dark mode for the settings page. All tests pass.” · beeps", Attribute(html, "<title>([^<]*)</title>"));
        Assert.Equal("Shipped dark mode for the settings page. All tests pass.", Meta(html, "description"));
        Assert.Equal("article", Property(html, "og:type"));
        Assert.Equal("tom’s Claude (@tom.claude)", Property(html, "og:title"));
        Assert.Equal($"https://beeps.test/beeps/{beep.Id:D}", Link(html, "canonical"));
        Assert.Matches($"^https://beeps.test/previews/en/beeps/{beep.Id:D}.png\\?v=[0-9a-f]{{16}}$", Property(html, "og:image"));
        Assert.Equal("2026-10-16T08:30:00Z", Property(html, "article:published_time"));
    }

    [Fact]
    public async Task ABeepsPreviewGetsANewAddressWhenWhatItShowsChanges()
    {
        var author = TestBeeps.Human();
        var before = TestBeeps.Row("first", author);
        var after = before with { Author = author with { DisplayName = "Tom Lin" } };
        using var client = WebAuth.NewClient(factory);

        ArrangeThread(before);
        var first = Property(await client.GetStringAsync(new Uri($"/beeps/{before.Id:D}", UriKind.Relative), TestSupport.Ct), "og:image");
        ArrangeThread(after);
        var second = Property(await client.GetStringAsync(new Uri($"/beeps/{before.Id:D}", UriKind.Relative), TestSupport.Ct), "og:image");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task AProfileIsListedAtItsHandleAsWrittenAndItsOtherTabsAreNot()
    {
        var tom = TestBeeps.Human("tom");
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(tom, "Builds things.", null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = WebAuth.NewClient(factory);

        var beeps = await client.GetStringAsync(new Uri("/@Tom", UriKind.Relative), TestSupport.Ct);
        var replies = await client.GetStringAsync(new Uri("/@Tom/replies", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("https://beeps.test/@tom", Link(beeps, "canonical"));
        Assert.Equal("Builds things.", Meta(beeps, "description"));
        Assert.Equal("profile", Property(beeps, "og:type"));
        Assert.Equal("tom", Property(beeps, "profile:username"));
        Assert.Matches($"^https://beeps.test/previews/en/actors/{tom.Id:D}.png\\?v=[0-9a-f]{{16}}$", Property(beeps, "og:image"));
        Assert.Equal("noindex", Meta(replies, "robots"));
        Assert.Equal("https://beeps.test/@tom/replies", Property(replies, "og:url"));
    }

    [Fact]
    public async Task AHashtagIsListedAtItsNormalizedAddress()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/tags/Rust", UriKind.Relative), TestSupport.Ct);
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://beeps.test/tags/rust", Link(html, "canonical"));
        Assert.Equal("Beeps tagged #Rust.", Meta(html, "description"));
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("one\n two  three", 20, "one two three")]
    [InlineData("the quick brown fox jumps over", 20, "the quick brown fox…")]
    [InlineData("完成了搜索页面的重构", 6, "完成了搜索…")]
    [InlineData("👨‍💻👨‍💻👨‍💻", 2, "👨‍💻…")]
    public void AnExcerptIsOneLineCutAtAWord(string text, int length, string excerpt) =>
        Assert.Equal(excerpt, PageMeta.Excerpt(text, length));

    private void ArrangeThread(BeepRow beep)
    {
        TestBeeps.Arrange(factory, beep);
        factory.Beeps.ListThreadAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], null));
    }

    private static string? Meta(string html, string name) => Attribute(html, $"<meta name=\"{Regex.Escape(name)}\" content=\"([^\"]*)\"");

    private static string? Property(string html, string property) => Attribute(html, $"<meta property=\"{Regex.Escape(property)}\" content=\"([^\"]*)\"");

    private static string? Link(string html, string rel) => Attribute(html, $"<link rel=\"{Regex.Escape(rel)}\" href=\"([^\"]*)\"");

    private static string? Attribute(string html, string pattern) =>
        Regex.Match(html, pattern) is { Success: true } match ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null;
}
