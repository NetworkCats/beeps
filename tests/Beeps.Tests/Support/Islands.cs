using System.Text.Json;
using System.Text.RegularExpressions;
using Beeps.Pages.Islands;

namespace Beeps.Tests.Support;

/// <summary>
/// Reads the islands of a page (Pages/Islands): the props each one is drawn from, as the browser gets them. Either from
/// a page's HTML (signed out, a form posted without the app, the layout's islands) or from its data (<see cref="PageAsync"/>).
/// </summary>
internal static partial class Islands
{
    /// <summary>The envelope (<c>{"props": …, "errors": …}</c>) of the page's first island of that name.</summary>
    public static JsonElement Envelope(string page, string name)
    {
        if (IsData(page))
        {
            using var data = JsonDocument.Parse(page);
            return data.RootElement.GetProperty("islands").TryGetProperty(name, out var island)
                ? island.Clone()
                : throw new Xunit.Sdk.XunitException($"No island \"{name}\" on the page.");
        }

        var match = IslandPattern().Matches(page).FirstOrDefault(m => m.Groups["name"].Value == name)
            ?? throw new Xunit.Sdk.XunitException($"No island \"{name}\" on the page.");
        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        return document.RootElement.Clone();
    }

    public static JsonElement Props(string page, string name) => Envelope(page, name).GetProperty("props");

    /// <summary>The app config (Client/app/config.ts): where the interface text is, the antiforgery token, who is signed in.</summary>
    public static JsonElement Config(string page)
    {
        if (IsData(page))
        {
            return Data(page).GetProperty("config");
        }

        var match = AppConfigPattern().Match(page);
        Assert.True(match.Success, "The page has no app config.");
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        return document.RootElement.Clone();
    }

    public static IReadOnlyList<string> Names(string page)
    {
        if (!IsData(page))
        {
            return [.. IslandPattern().Matches(page).Select(m => m.Groups["name"].Value)];
        }

        using var data = JsonDocument.Parse(page);
        return [.. data.RootElement.GetProperty("islands").EnumerateObject().Select(p => p.Name)];
    }

    /// <summary>What a page's data says of it besides its islands (<c>page</c>: its content column, layout state, config).</summary>
    public static JsonElement Data(string page)
    {
        using var data = JsonDocument.Parse(page);
        return data.RootElement.GetProperty("page").Clone();
    }

    /// <summary>The data of the page at <paramref name="url"/>, which answered with a success, as the app reads it.</summary>
    public static async Task<string> PageAsync(HttpClient client, string url)
    {
        using var response = await GetAsync(client, url);
        Assert.True(response.IsSuccessStatusCode, $"{url} answered {(int)response.StatusCode}.");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync(TestSupport.Ct);
    }

    private static bool IsData(string page) => page.StartsWith('{');

    /// <summary>Where the app asks for the data of the page at <paramref name="url"/> (PageDataMiddleware).</summary>
    public static Uri DataUrl(string url) => new(PageDataMiddleware.Prefix + url, UriKind.Relative);

    /// <summary>A page's data, as the app gets it: its islands' props, content column and layout state, as JSON.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string url) => client.GetAsync(DataUrl(url), TestSupport.Ct);

    /// <summary>A form sent as the app sends it: the answer is the data of the page it leads to.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string pageUrl, string postUrl, params (string Name, string Value)[] fields)
    {
        var page = await client.GetStringAsync(DataUrl(pageUrl), TestSupport.Ct);
        using var content = new FormUrlEncodedContent(
            [.. fields.Select(f => KeyValuePair.Create(f.Name, f.Value)), KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(page))]);
        return await client.PostAsync(DataUrl(postUrl), content, TestSupport.Ct);
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
