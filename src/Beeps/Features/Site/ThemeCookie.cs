namespace Beeps.Features.Site;

/// <summary>The browser's colour scheme (<see cref="Themes"/>): <c>light</c> or <c>dark</c>; none follows the system.</summary>
public static class ThemeCookie
{
    public const string Name = "beeps.theme";

    /// <summary><see cref="Themes.Light"/>, <see cref="Themes.Dark"/>, or null for the system's.</summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Themes.Scheme(Themes.Find(request.Cookies[Name]) ?? Themes.System);
    }

    public static void Set(HttpResponse response, string? theme)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Themes.Scheme(Themes.Find(theme) ?? Themes.System) is not { } scheme)
        {
            response.Cookies.Delete(Name);
            return;
        }

        response.Cookies.Append(
            Name,
            scheme,
            new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                IsEssential = true,
                // Read by the server only: the page is drawn with it, and theme.ts changes it through /theme.
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = response.HttpContext.Request.IsHttps,
            });
    }
}
