namespace Beeps.Pages.Islands;

/// <summary>
/// Pages are islands (PLAN 6.9), drawn by the browser's Preact components from their props. An islands request is a
/// page's data, asked for at <c>/page-data/{page}</c> (<see cref="PageDataMiddleware"/>, which marks it with
/// <see cref="Header"/>): the page runs as usual (same handlers, same queries), its islands write their props here
/// instead of their markup, and <c>_Islands.cshtml</c> answers with them as JSON, with what else the app needs to show
/// the page. That is how the app shows a page, how an island catches up with what changed, and how a form posted
/// from one gets its answer.
/// </summary>
public static class IslandRequests
{
    /// <summary>Set by <see cref="PageDataMiddleware"/> alone; cached public pages vary by it (PublicPageCache).</summary>
    public const string Header = "X-Beeps-Islands";

    private static readonly object CollectorKey = new();

    /// <summary>Signed out too: a list's next page, say.</summary>
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
