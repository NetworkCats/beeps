using Beeps.Features.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Beeps.Infrastructure.Email;

/// <summary>Local development sender (MailHog). SMTP has no idempotency keys, so a retried send may arrive twice.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(settings.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            settings.Smtp.Host,
            settings.Smtp.Port,
            settings.Smtp.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            await client.AuthenticateAsync(settings.Smtp.UserName, settings.Smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
