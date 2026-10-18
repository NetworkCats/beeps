using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using Beeps.Features.Email;
using Beeps.Features.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>
/// Outbox payload for a verification, password-reset or email-change code. The code itself is encrypted with Data
/// Protection: the database keeps only hashes of codes (PLAN 6.13), and the outbox is in the database.
/// </summary>
public sealed record AccountCodeEmail(string To, string Locale, EmailCodePurpose Purpose, string ProtectedCode)
{
    public const string MessageType = "accounts.code_email";

    public const string ProtectorPurpose = "Beeps.Accounts.EmailCode.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AccountCodeEmail))]
internal sealed partial class AccountsJsonContext : JsonSerializerContext;

/// <summary>Sends the code email in the recipient's language.</summary>
public sealed class AccountCodeEmailHandler(
    IEmailSender sender,
    IDataProtectionProvider dataProtection,
    IStringLocalizer<SharedResource> localizer) : IOutboxHandler
{
    private readonly IDataProtector protector = dataProtection.CreateProtector(AccountCodeEmail.ProtectorPurpose);

    public string Type => AccountCodeEmail.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = message.ReadPayload(AccountsJsonContext.Default.AccountCodeEmail);
        var code = protector.Unprotect(payload.ProtectedCode);
        // The outbox id stays the same across retries, so the provider can drop a duplicate send.
        await sender.SendAsync(Compose(payload, code), message.Id.ToString("D"), cancellationToken);
    }

    public EmailMessage Compose(AccountCodeEmail payload, string code)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(payload.Locale);
        try
        {
            var prefix = payload.Purpose switch
            {
                EmailCodePurpose.VerifyEmail => "Email.Verify",
                EmailCodePurpose.ResetPassword => "Email.Reset",
                EmailCodePurpose.ChangeEmail => "Email.Change",
                _ => throw new ArgumentOutOfRangeException(nameof(payload), payload.Purpose, null),
            };
            var subject = localizer[$"{prefix}.Subject", code].Value;
            var intro = localizer[$"{prefix}.Intro"].Value;
            var expiry = localizer["Email.Code.Expiry", (int)AccountService.CodeLifetime.TotalMinutes].Value;
            var ignore = localizer[$"{prefix}.Ignore"].Value;
            var text = $"{intro}\n\n{code}\n\n{expiry}\n\n{ignore}\n";
            var html =
                $"<p>{WebUtility.HtmlEncode(intro)}</p>" +
                $"<p><strong>{WebUtility.HtmlEncode(code)}</strong></p>" +
                $"<p>{WebUtility.HtmlEncode(expiry)}</p>" +
                $"<p>{WebUtility.HtmlEncode(ignore)}</p>";
            return new EmailMessage(payload.To, subject, text, html);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
