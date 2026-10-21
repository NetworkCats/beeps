using System.Text.RegularExpressions;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Beeps.Pages.Islands;

/// <summary>
/// The client's files under <c>dist/</c>, before MapStaticAssets serves them. What only the admin pages use (their
/// stylesheet and chunks, <see cref="ClientChunks.IsAdminOnly"/>) is given to admins only, and kept by no shared cache;
/// to anyone else it is not there. The other chunks are named by a hash of their content (scripts/build-client.ts) but
/// imported by those names, not by MapStaticAssets' fingerprinted ones, which it would serve as no-cache: a chunk never
/// changes, so it is kept for good. The chunk manifest is the server's own.
/// </summary>
public sealed partial class ClientAssetsMiddleware(RequestDelegate next)
{
    private const string KeptForGood = "max-age=31536000, immutable";

    public async Task InvokeAsync(HttpContext context, ClientChunks chunks, IAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!context.Request.Path.StartsWithSegments("/dist", out var rest) || !rest.HasValue)
        {
            await next(context);
            return;
        }

        // admin.ab12cd34ef.css, admin.css.gz and chunks/X.js.ab12cd34ef.map are the same file as admin.css and chunks/X.js
        // (MapStaticAssets serves each file fingerprinted, compressed and as its source map, in any mix). Routing finds
        // static files whatever the case of their address (ADMIN.CSS is admin.css), and passes over empty segments
        // (admin.css/ is admin.css), so the file is named in one case and without them here.
        var name = string.Join('/', rest.Value.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var file = Fingerprint().Replace(Variant().Replace(name, ""), "");
        var adminOnly = chunks.IsAdminOnly(file);
        if (file == "chunks.json" || (adminOnly && !(await authorization.AuthorizeAsync(context.User, AuthPolicies.Admin)).Succeeded))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (adminOnly || file.StartsWith("chunks/", StringComparison.Ordinal))
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                {
                    context.Response.Headers.CacheControl = $"{(adminOnly ? "private" : "public")}, {KeptForGood}";
                }

                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    [GeneratedRegex(@"(?:(?:\.[a-z0-9]{10})?\.map)?(?:\.(?:gz|br))?$")]
    private static partial Regex Variant();

    [GeneratedRegex(@"\.[a-z0-9]{10}(?=\.(?:css|js|json)$)")]
    private static partial Regex Fingerprint();
}
