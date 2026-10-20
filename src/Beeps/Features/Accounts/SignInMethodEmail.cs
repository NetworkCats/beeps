using System.Globalization;
using System.Net;
using Beeps.Features.Email;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>
/// Outbox payload: a way to sign in was added to the account (a passkey, or Google or GitHub). Whoever held a stolen
/// session could add their own and keep signing in after the password is reset; the owner hears of it this way.
/// </summary>
/// <param name="Provider">The provider's name; null for a passkey.</param>
public sealed record SignInMethodEmail(string To, string Locale, string? Provider)
{
    public const string MessageType = "accounts.sign_in_method_email";
}

/// <summary>Sends the notice in the recipient's language.</summary>
public sealed class SignInMethodEmailHandler(IEmailSender sender, IStringLocalizer<SharedResource> localizer) : IOutboxHandler
{
    public string Type => SignInMethodEmail.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.ReadPayload(AccountsJsonContext.Default.SignInMethodEmail);
        // The outbox id stays the same across retries, so the provider can drop a duplicate send.
        await sender.SendAsync(Compose(payload), message.Id.ToString("D"), cancellationToken);
    }

    public EmailMessage Compose(SignInMethodEmail payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(payload.Locale);
        try
        {
            var method = payload.Provider ?? localizer["Email.MethodAdded.Passkey"].Value;
            var subject = localizer["Email.MethodAdded.Subject"].Value;
            var intro = localizer["Email.MethodAdded.Intro", method].Value;
            var ignore = localizer["Email.MethodAdded.Ignore"].Value;
            var text = $"{intro}\n\n{ignore}\n";
            var html = $"<p>{WebUtility.HtmlEncode(intro)}</p><p>{WebUtility.HtmlEncode(ignore)}</p>";
            return new EmailMessage(payload.To, subject, text, html);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
