namespace Beeps.Features.Site;

/// <summary>The browser's colour scheme (<see cref="Themes"/>): <c>light</c> or <c>dark</c>; none follows the system.</summary>
public static class ThemeCookie
{
    /// <summary><see cref="Themes.Light"/>, <see cref="Themes.Dark"/>, or null for the system's.</summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Themes.Scheme(Themes.Find(request.Cookies[CookieNames.Theme]) ?? Themes.System);
    }

    public static void Set(HttpResponse response, string? theme)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Themes.Scheme(Themes.Find(theme) ?? Themes.System) is not { } scheme)
        {
            response.Cookies.Delete(CookieNames.Theme);
            return;
        }

        PreferenceCookies.Append(response, CookieNames.Theme, scheme);
    }
}
