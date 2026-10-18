namespace Beeps.Pages.Islands;

/// <summary>
/// The app's JSON API for the site's pages (PLAN 6.9): <c>/page-data/{page}</c> answers with the data of the page at
/// <c>/{page}</c> (same query), as JSON. The request is handed on as one for the page itself, marked as an islands
/// request (<see cref="IslandRequests"/>): the page runs as usual (same handlers, same queries) and answers with what
/// its islands are drawn from instead of markup. A redirect stays under <c>/page-data</c>, so the app, following it,
/// gets the data of the page it leads to. Ahead of the error pages, so a page not found answers as JSON too.
/// </summary>
public sealed class PageDataMiddleware(RequestDelegate next)
{
    public const string Prefix = "/page-data";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        // The mark is this middleware's to set: no request brings it along.
        request.Headers.Remove(IslandRequests.Header);
        if (!request.Path.StartsWithSegments(Prefix, out var page))
        {
            return next(context);
        }

        request.Path = page.HasValue ? page : "/";
        request.Headers[IslandRequests.Header] = "1";
        context.Response.OnStarting(() =>
        {
            if (UnderPrefix(request, context.Response.Headers.Location.ToString()) is { } location)
            {
                context.Response.Headers.Location = location;
            }

            return Task.CompletedTask;
        });
        return next(context);
    }

    /// <summary>A redirect to a page of this site, as the redirect to that page's data; null for anything else.</summary>
    private static string? UnderPrefix(HttpRequest request, string location)
    {
        var pathBase = request.PathBase.Value ?? "";
        if (location.StartsWith('/'))
        {
            return location.StartsWith("//", StringComparison.Ordinal) || !location.StartsWith($"{pathBase}/", StringComparison.Ordinal)
                ? null
                : $"{pathBase}{Prefix}{location[pathBase.Length..]}";
        }

        // The sign-in challenge's is absolute.
        return Uri.TryCreate(location, UriKind.Absolute, out var url)
            && url.Scheme == request.Scheme
            && string.Equals(url.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            && UnderPrefix(request, url.PathAndQuery + url.Fragment) is { } path
                ? $"{url.Scheme}://{url.Authority}{path}"
                : null;
    }
}
