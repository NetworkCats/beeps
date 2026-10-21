using System.Globalization;
using System.Net;
using Beeps.Features.Localization;
using Beeps.Features.Site;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Email;

/// <summary>What an email says; <see cref="EmailLayout"/> lays it out.</summary>
/// <param name="Title">A short heading, like the site's column header.</param>
/// <param name="Intro">The first paragraph; also the inbox preview.</param>
/// <param name="Outro">The last paragraph: what to do if it wasn't you.</param>
/// <param name="Code">A one-time code, set apart in large type.</param>
/// <param name="CodeNote">A quiet line under the code, such as how long it lasts.</param>
public sealed record EmailContent(string Subject, string Title, string Intro, string Outro, string? Code = null, string? CodeNote = null);

/// <summary>
/// The one look every beeps email has, the site's: a flat white page (near-black in dark mode where the client
/// follows it), ink and one secondary gray, a small bold title, the logo on top and the site in the footer.
/// Styles are inline and tables only centre the column, since mail clients drop most CSS.
/// </summary>
public sealed class EmailLayout(IStringLocalizer<SharedResource> localizer, IOptions<SiteOptions> site)
{
    // The site's tokens (_theme.scss) in hex: mail clients know no oklch().
    private const string Font =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Hiragino Sans GB','Microsoft YaHei','Noto Sans SC',sans-serif";

    private const string Mono = "ui-monospace,'SF Mono',Menlo,Consolas,'Liberation Mono',monospace";

    /// <summary>
    /// Writes the email in <paramref name="locale"/>, the recipient's saved language: <paramref name="write"/> and the
    /// layout's own words run under it. A language beeps no longer offers falls back to the default.
    /// </summary>
    public EmailMessage Compose(string to, string locale, Func<EmailContent> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(SupportedCultures.Find(locale) ?? SupportedCultures.Default);
        try
        {
            var content = write();
            return new EmailMessage(to, content.Subject, Text(content), Html(content));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private string Text(EmailContent content)
    {
        string?[] parts = [content.Title, content.Intro, content.Code, content.CodeNote, content.Outro, Footer() + "\n" + Home()];
        return string.Join("\n\n", parts.OfType<string>()) + "\n";
    }

    private string Html(EmailContent content)
    {
        var lang = CultureInfo.CurrentUICulture.Name;
        var home = E(site.Value.BaseUri.ToString());
        var logo = E(site.Value.Absolute("/images/logo.png").ToString());
        var code = content.Code is null
            ? ""
            : $$"""
              <div class="well ink" style="margin:0 0 8px;padding:14px 20px;background:#f5f5f5;border-radius:12px;color:#121212;font-family:{{Mono}};font-size:30px;line-height:1.2;font-weight:700;letter-spacing:6px;">{{E(content.Code)}}</div>
              <p class="muted" style="margin:0 0 20px;font-size:13px;line-height:1.5;color:#5b5b5b;">{{E(content.CodeNote ?? "")}}</p>
              """;
        return $$"""
            <!doctype html>
            <html lang="{{lang}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            <title>{{E(content.Subject)}}</title>
            <style>
            @media (prefers-color-scheme: dark) {
              .bg { background: #0a0a0a !important; }
              .ink { color: #eeeeee !important; }
              .muted { color: #9e9e9e !important; }
              .well { background: #171717 !important; }
              .link { color: #8191f7 !important; }
            }
            @media (max-width: 520px) {
              .page { padding: 24px 16px !important; }
            }
            </style>
            </head>
            <body class="bg" style="margin:0;padding:0;background:#ffffff;">
            <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;">{{E(content.Intro)}}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" style="background:#ffffff;">
            <tr><td align="center" class="page" style="padding:40px 24px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:480px;font-family:{{Font}};">
            <tr><td style="padding-bottom:28px;">
              <a href="{{home}}" style="text-decoration:none;"><img src="{{logo}}" width="32" height="32" alt="" style="display:inline-block;border:0;vertical-align:middle;"><span class="ink" style="margin-left:8px;color:#121212;font-size:17px;font-weight:700;vertical-align:middle;">{{E(localizer["Site.Name"].Value)}}</span></a>
            </td></tr>
            <tr><td class="ink" style="color:#121212;">
              <h1 style="margin:0 0 12px;font-size:20px;line-height:1.3;font-weight:800;letter-spacing:-0.01em;">{{E(content.Title)}}</h1>
              <p style="margin:0 0 20px;font-size:15px;line-height:1.5;">{{E(content.Intro)}}</p>
              {{code}}
              <p style="margin:0;font-size:15px;line-height:1.5;">{{E(content.Outro)}}</p>
            </td></tr>
            <tr><td class="muted" style="padding-top:40px;color:#5b5b5b;font-size:13px;line-height:1.5;">
              <a href="{{home}}" class="link" style="color:#4c52d0;text-decoration:none;">{{E(localizer["Site.Name"].Value)}}</a> · {{E(localizer["Site.Tagline"].Value)}}
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    private string Footer() => $"{localizer["Site.Name"].Value} · {localizer["Site.Tagline"].Value}";

    private string Home() => site.Value.BaseUri.ToString().TrimEnd('/');

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
