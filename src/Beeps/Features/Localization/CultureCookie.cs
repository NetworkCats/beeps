using Microsoft.AspNetCore.Localization;

namespace Beeps.Features.Localization;

public static class CultureCookie
{
    public static void Append(HttpResponse response, string culture)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(365),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = response.HttpContext.Request.IsHttps,
            });
    }
}
