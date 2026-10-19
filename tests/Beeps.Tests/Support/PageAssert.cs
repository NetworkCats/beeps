namespace Beeps.Tests.Support;

internal static class PageAssert
{
    public static void NoInlineScriptsOrStyles(string html)
    {
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" onclick=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        // Scripts come from files; the islands' props and the app's config are JSON data, which never runs (PLAN 6.9).
        Assert.DoesNotMatch("<script(?![^>]*\\b(src=|type=\"application/json\"))", html);
    }
}
