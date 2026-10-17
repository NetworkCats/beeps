using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beeps.Features.Localization;
using Beeps.Features.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Islands;

/// <summary>
/// The interface text the app's components show, one JSON document per language at <c>/strings/{culture}.json</c>,
/// apart from the pages: the app keeps it in the browser (<c>Client/app/i18n.ts</c>) and fetches it again only when it
/// changes. It comes from the same resource files as the pages'; the keys the components use are listed in
/// <c>Client/strings.json</c> (embedded), so only those are sent. The address the layout names carries a hash of the
/// document (<c>?v=</c>), so a browser or the CDN keeps it for good, and a new deployment that changes it is a new address.
/// </summary>
public sealed class ClientStrings(IStringLocalizer<SharedResource> localizer)
{
    public const string Path = "/strings";

    public static readonly IReadOnlyList<string> Keys = LoadKeys();

    private readonly ConcurrentDictionary<string, Document> documents = new(StringComparer.Ordinal);

    /// <param name="Version">The first characters of the document's SHA-256, in hex.</param>
    public sealed record Document(byte[] Json, string Version);

    /// <summary>The document of a supported culture.</summary>
    public Document For(string culture) => documents.GetOrAdd(culture, Build);

    /// <summary>The address of the document in the page's language.</summary>
    public string UrlFor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var culture = SupportedCultures.Find(CultureInfo.CurrentUICulture.Name) ?? SupportedCultures.Default;
        return $"{context.Request.PathBase}{Path}/{culture}.json?v={For(culture).Version}";
    }

    private Document Build(string culture)
    {
        // The localizer reads the current thread's language.
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var text = Keys.ToDictionary(key => key, key => localizer[key].Value, StringComparer.Ordinal);
            var json = Encoding.UTF8.GetBytes(IslandJson.Serialize(text));
            return new Document(json, Convert.ToHexStringLower(SHA256.HashData(json))[..10]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static List<string> LoadKeys()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beeps.client-strings.json")
            ?? throw new InvalidOperationException("Client/strings.json is not embedded.");
        return JsonSerializer.Deserialize(stream, IslandJsonContext.Default.ListString) ?? [];
    }
}

public static class ClientStringsEndpoints
{
    private const string KeptForGood = "public, max-age=31536000, immutable";

    public static IEndpointRouteBuilder MapClientStrings(this IEndpointRouteBuilder routes)
    {
        // The same for everyone and cheap, and kept by the CDN, as the scripts are.
        routes.MapGet($"{ClientStrings.Path}/{{culture}}.json", Get).WithoutRateLimit().ExcludeFromDescription();
        return routes;
    }

    private static IResult Get(string culture, string? v, ClientStrings strings, HttpResponse response)
    {
        if (SupportedCultures.Find(culture) != culture)
        {
            return Results.NotFound();
        }

        var document = strings.For(culture);
        // An address from before the text changed (a page drawn before a deployment) gets the text as it is now, but
        // not for keeps.
        response.Headers.CacheControl = v == document.Version ? KeptForGood : "no-cache";
        return Results.Bytes(document.Json, "application/json; charset=utf-8");
    }
}
