using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Email;

public enum EmailProvider
{
    /// <summary>Local development: MailHog on localhost:1025.</summary>
    Smtp,

    /// <summary>Production: the Resend API.</summary>
    Resend,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    /// <summary>Sender, e.g. <c>beeps &lt;noreply@beeps.sh&gt;</c>.</summary>
    public string From { get; set; } = "";

    public SmtpEmailOptions Smtp { get; set; } = new();

    public string ResendApiToken { get; set; } = "";
}

public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 25;

    /// <summary>Off for MailHog; on for any real server.</summary>
    public bool UseTls { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }
}

internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.From))
        {
            failures.Add("Email:From is required.");
        }

        if (options.Provider == EmailProvider.Resend && string.IsNullOrWhiteSpace(options.ResendApiToken))
        {
            failures.Add("Email:ResendApiToken is required when Email:Provider is Resend.");
        }

        if (options.Provider == EmailProvider.Smtp && string.IsNullOrWhiteSpace(options.Smtp.Host))
        {
            failures.Add("Email:Smtp:Host is required when Email:Provider is Smtp.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
