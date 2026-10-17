using System.Net;
using System.Text.Json;
using Beeps.Pages.Islands;
using Beeps.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

/// <summary>The app's interface text, fetched apart from the pages and kept by the browser (Pages/Islands/ClientStrings.cs).</summary>
public sealed class ClientStringsTests(BeepsWebApplicationFactory factory) : IClassFixture<BeepsWebApplicationFactory>
{
    [Fact]
    public async Task ThePageNamesTheTextInItsLanguageWhichIsKeptForGood()
    {
        using var client = factory.CreateClient();
        using var page = new HttpRequestMessage(HttpMethod.Get, "/public");
        page.Headers.Add("Accept-Language", "zh-CN");
        var html = await (await client.SendAsync(page, TestSupport.Ct)).Content.ReadAsStringAsync(TestSupport.Ct);
        var url = Islands.Config(html).GetProperty("stringsUrl").GetString()!;
        Assert.StartsWith("/strings/zh-Hans.json?v=", url, StringComparison.Ordinal);

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var text = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal(ClientStrings.Keys.Order(StringComparer.Ordinal), text.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("返回", text.RootElement.GetProperty("Nav.Back").GetString());
    }

    [Fact]
    public async Task AnOutdatedAddressGetsTheTextAsItIsNowButNotForKeeps()
    {
        using var client = factory.CreateClient();
        var current = factory.Services.GetRequiredService<ClientStrings>().For("en");

        using var response = await client.GetAsync(new Uri("/strings/en.json?v=0000000000", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal(current.Json, await response.Content.ReadAsByteArrayAsync(TestSupport.Ct));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("zh-hans")]
    public async Task OnlyTheSupportedCulturesHaveText(string culture)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/strings/{culture}.json", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
