using System.Globalization;
using Beeps.Features.Auth;
using Beeps.Features.Crawlers;

namespace Beeps.Features.Translation;

/// <summary>
/// How the signed-out visitor of this request reads beeps (<see cref="Beeps.BeepViews"/>): translated into the language
/// they see the site in. Null for a crawler, which indexes beeps as written rather than machine translations of them,
/// for a signed-in request (its viewer says), and for work off a request.
/// </summary>
public sealed class VisitorLanguages
{
    public ReaderLanguages? Current { get; set; }

    /// <summary>After request localization, so the culture is the visitor's. Public pages cache a copy per culture and for crawlers apart.</summary>
    public static IApplicationBuilder UseVisitorLanguages(IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true && !context.FromCrawler())
            {
                context.RequestServices.GetRequiredService<VisitorLanguages>().Current = ReaderLanguages.Visitor(CultureInfo.CurrentUICulture.Name);
            }

            return next(context);
        });
}
