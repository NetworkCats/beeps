using System.Text.RegularExpressions;

namespace Beeps.Tests.Support;

internal static partial class TestSupport
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The antiforgery token from a rendered form.</summary>
    public static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        Assert.True(match.Success, "The page has no antiforgery token.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)" """)]
    private static partial Regex AntiforgeryTokenPattern();
}
