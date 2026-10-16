namespace Beeps.Features.Email;

/// <summary>A ready-to-send email: both a plain-text and an HTML body.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Delivers email: Resend in production, SMTP (MailHog) locally. Callers never know which.</summary>
public interface IEmailSender
{
    /// <param name="idempotencyKey">Same key, same email: a retried outbox message must not send twice where the provider can prevent it.</param>
    Task SendAsync(EmailMessage message, string idempotencyKey, CancellationToken cancellationToken);
}
