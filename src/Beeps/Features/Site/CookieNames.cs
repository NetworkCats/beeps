namespace Beeps.Features.Site;

/// <summary>
/// Every cookie the site sets, in one place. The framework's own (antiforgery, language, the notice after a redirect)
/// are renamed from its ".AspNetCore.…" defaults too, so no cookie tells what the site runs on.
/// </summary>
public static class CookieNames
{
    /// <summary>The sign-in session's token.</summary>
    public const string Session = "beeps.session";

    /// <summary>The colour scheme picked (<see cref="ThemeCookie"/>).</summary>
    public const string Theme = "beeps.theme";

    /// <summary>The language picked while signed out.</summary>
    public const string Language = "beeps.lang";

    /// <summary>The antiforgery token's cookie half.</summary>
    public const string Antiforgery = "beeps.csrf";

    /// <summary>The one-line notice a redirect leaves for the page it leads to (TempData).</summary>
    public const string Notice = "beeps.notice";

    /// <summary>Who Google or GitHub said signed in, until signing in or up with it is done (Features/Auth/ExternalAuth.cs).</summary>
    public const string External = "beeps.external";

    /// <summary>
    /// The start of the cookie that ties the provider's answer to the browser that went there (one per sign-in under
    /// way, named after it).
    /// </summary>
    public const string Correlation = "beeps.correlation.";
}
