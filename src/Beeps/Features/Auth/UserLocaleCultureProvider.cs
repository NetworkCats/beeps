using System.Security.Claims;
using Microsoft.AspNetCore.Localization;

namespace Beeps.Features.Auth;

/// <summary>The signed-in user's saved language; first in line, before the cookie and the browser (PLAN 6.12).</summary>
public sealed class UserLocaleCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var locale = httpContext.User.FindFirstValue(BeepsClaims.Locale);
        return locale is null ? NullProviderCultureResult : Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(locale));
    }
}
