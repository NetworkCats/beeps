using System.Text.Encodings.Web;
using System.Text.Unicode;
using Beeps.Features.Auth;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

namespace Beeps.Features.Localization;

public static class LocalizationSetup
{
    public static IServiceCollection AddBeepsLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedCultures.Default);
            options.AddSupportedCultures([.. SupportedCultures.All]);
            options.AddSupportedUICultures([.. SupportedCultures.All]);
            // zh-CN / zh-SG resolve to zh-Hans through their parent culture.
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
            // PLAN 6.12: user setting → cookie → Accept-Language → default.
            options.RequestCultureProviders =
            [
                new UserLocaleCultureProvider(),
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });
        // Razor would otherwise write every Chinese character as an &#x…; entity.
        services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
        return services;
    }
}
