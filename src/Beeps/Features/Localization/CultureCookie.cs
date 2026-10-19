using Beeps.Features.Site;
using Microsoft.AspNetCore.Localization;

namespace Beeps.Features.Localization;

public static class CultureCookie
{
    public static void Append(HttpResponse response, string culture) =>
        PreferenceCookies.Append(response, CookieNames.Language, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)));
}
