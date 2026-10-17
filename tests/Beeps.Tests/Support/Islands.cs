using System.Text.Json;
using System.Text.RegularExpressions;
using Beeps.Pages.Islands;

namespace Beeps.Tests.Support;

/// <summary>Reads the islands of a signed-in page (Pages/Islands): the props each one is drawn from, as the browser gets them.</summary>
internal static partial class Islands
{
    /// <summary>The envelope (<c>{"props": …, "errors": …}</c>) of the page's first island of that name.</summary>
    public static JsonElement Envelope(string html, string name)
    {
        var match = IslandPattern().Matches(html).FirstOrDefault(m => m.Groups["name"].Value == name)
            ?? throw new Xunit.Sdk.XunitException($"No island \"{name}\" on the page.");
        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        return document.RootElement.Clone();
    }

    public static JsonElement Props(string html, string name) => Envelope(html, name).GetProperty("props");

    /// <summary>The layout's app config (Client/app/config.ts): interface text, the antiforgery token, who is signed in.</summary>
    public static JsonElement Config(string html)
    {
        var match = AppConfigPattern().Match(html);
        Assert.True(match.Success, "The page has no app config.");
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        return document.RootElement.Clone();
    }

    public static IReadOnlyList<string> Names(string html) => [.. IslandPattern().Matches(html).Select(m => m.Groups["name"].Value)];

    /// <summary>An islands request: the page's props alone, as JSON.</summary>
    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Add(IslandRequests.Header, "1");
        return await client.SendAsync(request, TestSupport.Ct);
    }

    /// <summary>A form sent as the app sends it: the answer is the props of the page it leads to.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl, params (string Name, string Value)[] fields)
    {
        var html = await client.GetStringAsync(new Uri(pageUrl, UriKind.Relative), TestSupport.Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(postUrl, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(
                [.. fields.Select(f => KeyValuePair.Create(f.Name, f.Value)), KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html))]),
        };
        request.Headers.Add(IslandRequests.Header, "1");
        return await client.SendAsync(request, TestSupport.Ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        return document.RootElement.Clone();
    }

    public static IReadOnlyList<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString() ?? "")];

    [GeneratedRegex("<(?:div|span) class=\"island\" data-island=\"(?<name>[a-z-]+)\"><script data-island-props=\"\" type=\"application/json\">(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex IslandPattern();

    [GeneratedRegex("<script type=\"application/json\" id=\"app-config\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex AppConfigPattern();
}
