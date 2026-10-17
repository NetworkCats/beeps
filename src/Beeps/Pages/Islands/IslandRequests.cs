namespace Beeps.Pages.Islands;

/// <summary>
/// Pages are islands (PLAN 6.9): the server draws them, with each island's props beside it, and the browser's Preact
/// components take over from there. A request with <see cref="Header"/> asks for the props alone: the page runs as usual (same handlers, same queries), its islands write their props here instead
/// of their markup, and <c>_Islands.cshtml</c> answers with them as JSON. That is how an island catches up with what
/// changed, and how a form posted from one gets its answer.
/// </summary>
public static class IslandRequests
{
    public const string Header = "X-Beeps-Islands";

    private static readonly object CollectorKey = new();

    /// <summary>Signed out too: a list's next page, say. Cached public pages vary by this header (PublicPageCache).</summary>
    public static bool IsIslandRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Headers.ContainsKey(Header);
    }

    /// <summary>The islands the page has drawn so far in this request, by name, each as its JSON envelope.</summary>
    public static Dictionary<string, string> Collected(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items[CollectorKey] is not Dictionary<string, string> islands)
        {
            islands = new Dictionary<string, string>(StringComparer.Ordinal);
            context.Items[CollectorKey] = islands;
        }

        return islands;
    }
}
