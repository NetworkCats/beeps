using Beeps.Features.Email;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Accounts;

/// <summary>
/// Outbox payload: a way to sign in was added to the account (a passkey, or Google or GitHub). Whoever held a stolen
/// session could add their own and keep signing in after the password is reset; the owner hears of it this way.
/// </summary>
/// <param name="Provider">The provider's name; null for a passkey.</param>
/// <param name="TwoFactor">Passkeys were made the second step instead: from then on only they let anyone in.</param>
public sealed record SignInMethodEmail(string To, string Locale, string? Provider, bool TwoFactor = false)
{
    public const string MessageType = "accounts.sign_in_method_email";
}

/// <summary>Sends the notice in the recipient's language.</summary>
public sealed class SignInMethodEmailHandler(IEmailSender sender, EmailLayout layout, IStringLocalizer<SharedResource> localizer) : IOutboxHandler
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
        return layout.Compose(payload.To, payload.Locale, () =>
        {
            var prefix = payload.TwoFactor ? "Email.TwoFactorOn" : "Email.MethodAdded";
            var method = payload.Provider ?? localizer["Email.MethodAdded.Passkey"].Value;
            return new EmailContent(
                Subject: localizer[$"{prefix}.Subject"].Value,
                Title: localizer[$"{prefix}.Title"].Value,
                Intro: localizer[$"{prefix}.Intro", method].Value,
                Outro: localizer[$"{prefix}.Ignore"].Value);
        });
    }
}
