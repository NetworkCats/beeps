using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.Hosting;
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
        await SignInAsync(client, user);
        await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Show("second");
        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("second", html, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        cached.Dispose();
        factory.Dispose();
    }

    private void Show(string text) =>
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct)
            .ReturnsForAnyArgs(new CursorPage<BeepRow>([TestBeeps.Row(text, TestBeeps.Human())], null));

    private static async Task SignInAsync(HttpClient client, User user)
    {
        var html = await client.GetStringAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        using var form = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("Login", user.Actor.Handle!),
            KeyValuePair.Create("Password", WebAuth.Password),
            KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html)),
        ]);
        using var response = await client.PostAsync(new Uri("/login", UriKind.Relative), form, TestSupport.Ct);
    }
}
