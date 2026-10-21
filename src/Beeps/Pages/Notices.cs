using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Beeps.Pages;

/// <summary>TempData keys for the one-line message shown after a redirect (post/redirect/get).</summary>
public static class Notices
{
    public const string Success = "Notice";
    public const string Error = "Alert";

    /// <summary>
    /// The messages a redirect left for this response, read only when the request carries the TempData
    /// cookie: reading TempData otherwise makes the response delete a cookie that is not there, and a
    /// response that sets a cookie is never output-cached.
    /// </summary>
    public static (string? Success, string? Error) Take(HttpContext context, ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tempData);
        var cookie = context.RequestServices.GetRequiredService<IOptions<CookieTempDataProviderOptions>>().Value.Cookie.Name;
        return cookie is not null && context.Request.Cookies.ContainsKey(cookie)
            ? (tempData[Success] as string, tempData[Error] as string)
            : (null, null);
    }
}
