using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Beeps.Tests.Pages;

/// <summary>PLAN 6.6: public pages are cached for signed-out visitors only. Own factory, with the cache on.</summary>
public sealed class OutputCacheTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();
    private readonly WebApplicationFactory<Program> cached;

    public OutputCacheTests() =>
        cached = factory.WithWebHostBuilder(builder => builder.UseSetting("PublicPageCache:Enabled", "true"));

    [Fact]
    public async Task SignedOutVisitorsGetTheCachedPage()
    {
        Show("first");
        using var client = cached.CreateClient();
        var before = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Show("second");
        using var response = await client.GetAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var after = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Contains("first", before, StringComparison.Ordinal);
        Assert.Contains("first", after, StringComparison.Ordinal);
        Assert.NotNull(response.Headers.Age);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task EachLanguageHasItsOwnCopy()
    {
        Show("first");
        using var english = cached.CreateClient();
        using var chinese = cached.CreateClient();
        chinese.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        await english.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var html = await chinese.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<html lang=\"zh-Hans\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedInPagesAreNeverCached()
    {
        var user = WebAuth.ArrangeUser(factory);
        Show("first");
        using var client = cached.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await WebAuth.SignInAsync(client, user);
        await client.GetStringAsync(Islands.DataUrl("/public"), TestSupport.Ct);

        Show("second");
        var page = await client.GetStringAsync(Islands.DataUrl("/public"), TestSupport.Ct);

        Assert.Contains("second", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACachedPageIsEvictedByWhatItShowsAndOnlyByThat()
    {
        var shown = Show("first");
        using var client = cached.CreateClient();
        await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        Show("second");
        var store = cached.Services.GetRequiredService<IOutputCacheStore>();

        await store.EvictByTagAsync(PageTags.Shows(Guid.NewGuid()), TestSupport.Ct);
        await store.EvictByTagAsync(PageTags.Beep(Guid.NewGuid()), TestSupport.Ct);
        var untouched = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        await store.EvictByTagAsync(PageTags.Shows(shown.Author.Id), TestSupport.Ct);
        var afterAuthor = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var third = Show("third");
        await store.EvictByTagAsync(PageTags.Beep(shown.Id), TestSupport.Ct);
        var afterOldBeep = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        await store.EvictByTagAsync(PageTags.Beep(third.Id), TestSupport.Ct);
        var afterNothingShown = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("first", untouched, StringComparison.Ordinal);
        Assert.Contains("second", afterAuthor, StringComparison.Ordinal);
        // The page now shows "second", not the first beep: evicting that one leaves it alone.
        Assert.Contains("second", afterOldBeep, StringComparison.Ordinal);
        Assert.Contains("second", afterNothingShown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryCachedPageIsTaggedWithTheBeepsAndAuthorsItShows()
    {
        var tom = TestBeeps.Human("tom");
        var shown = TestBeeps.Row("first #news", tom);
        var later = TestBeeps.Row("second #news", tom, replyTo: shown.Id, root: shown.Id);
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(tom, null, null, TestData.Now, null));
        TestBeeps.Arrange(factory, shown, later);
        ShowEverywhere([shown]);
        var store = cached.Services.GetRequiredService<IOutputCacheStore>();
        using var client = cached.CreateClient();
        string[] paths = ["/public", "/@tom", "/tags/news", $"/beeps/{shown.Id}"];
        foreach (var path in paths)
        {
            await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct);
        }

        ShowEverywhere([shown, later]);
        foreach (var path in paths)
        {
            Assert.DoesNotContain("second", await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct), StringComparison.Ordinal);
        }

        await store.EvictByTagAsync(PageTags.Beep(shown.Id), TestSupport.Ct);
        foreach (var path in paths)
        {
            Assert.Contains("second", await client.GetStringAsync(new Uri(path, UriKind.Relative), TestSupport.Ct), StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        cached.Dispose();
        factory.Dispose();
    }

    private void ShowEverywhere(BeepRow[] rows)
    {
        var page = new CursorPage<BeepRow>(rows, null);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        factory.Timelines.ProfileAsync(default, default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        factory.Timelines.HashtagAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(page);
        factory.Beeps.ListThreadAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(page);
    }

    private BeepRow Show(string text)
    {
        var row = TestBeeps.Row(text, TestBeeps.Human());
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([row], null));
        return row;
    }
}
