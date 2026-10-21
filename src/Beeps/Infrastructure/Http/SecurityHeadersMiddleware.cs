using Microsoft.Net.Http.Headers;

namespace Beeps.Infrastructure.Http;

/// <summary>
/// The security headers every answer carries: pages, data, static files and error pages alike. Set as the answer
/// starts, so neither the exception handler (which clears the headers) nor antiforgery (which would frame-allow the
/// site's own pages, and only on the answer that sets its cookie) has the last word. HSTS is Cloudflare's (docs/INSTALL.md).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var headers = context.Response.Headers;
        context.Response.OnStarting(() =>
        {
            // Nothing of the site is framed, not even by itself: no page of it can be clicked through from another.
            headers[HeaderNames.XFrameOptions] = "DENY";
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // Sign-in with Google or GitHub leaves the page, not a popup: no other window needs a hold on this one.
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
