using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Beeps.Pages.Islands;

/// <summary>
/// The script chunks a page needs, from <c>wwwroot/dist/chunks.json</c> (written by <c>scripts/build-client.ts</c>):
/// those app.js imports, and for each island whose component is a chunk of its own those it needs beyond the app. A
/// page names them with <c>&lt;link rel="modulepreload"&gt;</c>, so they load alongside app.js instead of one after
/// another as each import is found. Without the file (the client not built yet) a page names none, and the imports
/// still load them. It also lists the files only the admin pages use, which <see cref="ClientAssetsMiddleware"/> gives
/// to admins only.
/// </summary>
public sealed class ClientChunks
{
    private const string ManifestPath = "dist/chunks.json";

    private static readonly object NamedKey = new();

    private readonly IFileProvider files;
    private volatile Manifest manifest;

    public ClientChunks(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        files = environment.WebRootFileProvider;
        manifest = Load();
        if (environment.IsDevelopment())
        {
            // `npm run watch` builds the client again while the site runs.
            ChangeToken.OnChange(() => files.Watch(ManifestPath), () => manifest = Load());
        }
    }

    /// <summary>The chunks app.js imports.</summary>
    public IHtmlContent ForApp(HttpContext context) => Links(context, manifest.App);

    /// <summary>The chunks of an island's component that this response has not named yet.</summary>
    public IHtmlContent ForIsland(HttpContext context, string island) =>
        Links(context, manifest.Islands.GetValueOrDefault(island) ?? []);

    /// <summary>Whether a file of <c>dist/</c> (<c>chunks/Overview-….js</c>, <c>admin.css</c>) is the admin pages' alone.</summary>
    public bool IsAdminOnly(string file) => manifest.Admin.Contains(file);

    private static HtmlContentBuilder Links(HttpContext context, IReadOnlyList<string> chunks)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items[NamedKey] is not HashSet<string> named)
        {
            named = new HashSet<string>(StringComparer.Ordinal);
            context.Items[NamedKey] = named;
        }

        var html = new HtmlContentBuilder();
        foreach (var chunk in chunks)
        {
            if (!named.Add(chunk))
            {
                continue;
            }

            // The very address app.js imports it from, not a fingerprinted one: the browser matches them by address.
            var link = new TagBuilder("link") { TagRenderMode = TagRenderMode.StartTag };
            link.Attributes["rel"] = "modulepreload";
            link.Attributes["href"] = $"{context.Request.PathBase}/dist/{chunk}";
            html.AppendHtml(link);
        }

        return html;
    }

    private Manifest Load()
    {
        var file = files.GetFileInfo(ManifestPath);
        if (!file.Exists)
        {
            return Manifest.Empty;
        }

        try
        {
            using var stream = file.CreateReadStream();
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            var islands = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var island in root.GetProperty("islands").EnumerateObject())
            {
                islands[island.Name] = Names(island.Value);
            }

            var admin = root.TryGetProperty("admin", out var listed) ? Names(listed) : [];
            return new Manifest(Names(root.GetProperty("app")), islands, admin.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Being written as it is read (npm run watch): the next change reads it whole.
            return Manifest.Empty;
        }
    }

    private static string[] Names(JsonElement array) => [.. array.EnumerateArray().Select(name => name.GetString() ?? "")];

    private sealed record Manifest(IReadOnlyList<string> App, IReadOnlyDictionary<string, IReadOnlyList<string>> Islands, FrozenSet<string> Admin)
    {
        // Not built yet: admin.css is still the admin pages' alone.
        public static readonly Manifest Empty = new([], new Dictionary<string, IReadOnlyList<string>>(), new[] { "admin.css" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
    }
}
