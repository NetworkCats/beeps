using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class CookieNamesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task EveryCookieIsNamedAfterBeepsNotTheFramework()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var signedOut = WebAuth.NewClient(factory);
        using var signedIn = await WebAuth.SignInAsync(factory, user);

        // The antiforgery cookie, the language, and the notice a redirect leaves.
        using var login = await signedOut.GetAsync(new Uri("/login", UriKind.Relative), TestSupport.Ct);
        using var language = await signedOut.GetAsync(new Uri("/culture?culture=zh-Hans&returnUrl=%2F", UriKind.Relative), TestSupport.Ct);
        using var saved = await WebAuth.PostFormAsync(signedIn, "/settings", "/settings?handler=Profile", ("DisplayName", "Tom"), ("Bio", ""));

        string[] names = [.. new[] { login, language, saved }
            .SelectMany(r => r.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
            .Select(c => c[..c.IndexOf('=', StringComparison.Ordinal)])
            .Distinct()];
        Assert.Superset(new HashSet<string>(["beeps.csrf", "beeps.lang", "beeps.notice"]), new HashSet<string>(names));
        Assert.All(names, name => Assert.StartsWith("beeps.", name, StringComparison.Ordinal));
    }

    public void Dispose() => factory.Dispose();
}
