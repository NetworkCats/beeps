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

        using var response = await PostAsync(client, "zh-Hans", "/some/page?x=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/some/page?x=1", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
        Assert.Contains("c%3Dzh-Hans%7Cuic%3Dzh-Hans", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalReturnUrlsAreIgnored()
    {
        using var client = NewClient();

        using var response = await PostAsync(client, "en", "https://evil.example/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task UnsupportedCulturesAreNotStored()
    {
        using var client = NewClient();

        using var response = await PostAsync(client, "fr", "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PostingWithoutAntiforgeryTokenIsRejected()
    {
        using var client = NewClient();
        using var form = new FormUrlEncodedContent([KeyValuePair.Create("culture", "zh-Hans")]);

        using var response = await client.PostAsync(new Uri("/culture", UriKind.Relative), form, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string culture, string returnUrl)
    {
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        using var form = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("culture", culture),
            KeyValuePair.Create("returnUrl", returnUrl),
            KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html)),
        ]);
        return await client.PostAsync(new Uri("/culture", UriKind.Relative), form, TestSupport.Ct);
    }
}
