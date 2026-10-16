using Beeps.Features.Email;
using Microsoft.Extensions.Options;
using Resend;

namespace Beeps.Infrastructure.Email;

/// <summary>Production sender: the Resend API, with the outbox message id as idempotency key.</summary>
public sealed class ResendEmailSender(IResend resend, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(Features.Email.EmailMessage message, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await resend.EmailSendAsync(
            idempotencyKey,
            new Resend.EmailMessage
            {
                From = options.Value.From,
                To = message.To,
                Subject = message.Subject,
                TextBody = message.TextBody,
                HtmlBody = message.HtmlBody,
            },
            cancellationToken);
    }
}
