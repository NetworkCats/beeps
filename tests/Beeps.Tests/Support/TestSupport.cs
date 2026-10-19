using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beeps.Tests.Support;

internal static partial class TestSupport
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The antiforgery token of a page: from a form the server drew, or, signed in, from the app's config, which the
    /// islands' forms carry it from.
    /// </summary>
    public static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        if (!match.Success)
        {
            match = AppConfigTokenPattern().Match(html);
        }

        Assert.True(match.Success, "The page has no antiforgery token.");
        return match.Groups[1].Value;
    }

    /// <summary>The JSON a response carries.</summary>
    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    /// <summary>Waits up to three seconds for work in the background to make <paramref name="condition"/> true.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(condition());
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)" """)]
    private static partial Regex AntiforgeryTokenPattern();

    [GeneratedRegex(@"""antiforgery"":\{""field"":""__RequestVerificationToken"",""token"":""([^""]+)""\}")]
    private static partial Regex AppConfigTokenPattern();
}
