using Microsoft.AspNetCore.Localization;

namespace Beeps.Features.Localization;

/// <summary>
/// Readers whose first language is any Chinese get the Simplified Chinese interface, the only Chinese one there is:
/// zh-TW, zh-HK and a bare zh do not fall back to zh-Hans through their parent cultures, so they would get English.
/// </summary>
public sealed class ChineseCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var first = httpContext.Request.GetTypedHeaders().AcceptLanguage
            .Where(l => l.Quality is not 0)
            .OrderByDescending(l => l.Quality ?? 1)
            .FirstOrDefault();
        var tag = first?.Value.Value;
        var chinese = tag is not null && (tag.Equals("zh", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("zh-", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(chinese ? new ProviderCultureResult(SupportedCultures.ChineseSimplified) : null);
    }
}
