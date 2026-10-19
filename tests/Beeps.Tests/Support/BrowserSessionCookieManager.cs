using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Beeps.Tests.Support;

/// <summary>
/// Writes the sign-in cookie without an expiry. It expires by the factory's clock, which can be fixed in the past, while
/// the test client's cookie jar drops it by the real one; without an expiry it lasts as long as the client.
/// </summary>
internal sealed class BrowserSessionCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager inner = new();

    public string? GetRequestCookie(HttpContext context, string key) => inner.GetRequestCookie(context, key);

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Expires = null;
        options.MaxAge = null;
        inner.AppendResponseCookie(context, key, value, options);
    }

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) => inner.DeleteCookie(context, key, options);
}
