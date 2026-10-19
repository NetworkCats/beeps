namespace Beeps.Features.Site;

/// <summary>The cookies that remember a choice in this browser, such as the colour scheme or the language.</summary>
public static class PreferenceCookies
{
    public static void Append(HttpResponse response, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(
            name,
            value,
            new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                IsEssential = true,
                // Read by the server only: the page is drawn with them, and the app changes them through the server.
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = response.HttpContext.Request.IsHttps,
            });
    }
}
