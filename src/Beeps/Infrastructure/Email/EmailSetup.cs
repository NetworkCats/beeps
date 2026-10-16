using Beeps.Features.Email;
using Microsoft.Extensions.Options;
using Resend;

namespace Beeps.Infrastructure.Email;

public static class EmailSetup
{
    public static IServiceCollection AddBeepsEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddResend(_ => { });
        services.AddOptions<ResendClientOptions>().Configure<IOptions<EmailOptions>>((resend, email) => resend.ApiToken = email.Value.ResendApiToken);
        services.AddTransient<ResendEmailSender>();
        services.AddTransient<SmtpEmailSender>();
        services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider switch
        {
            EmailProvider.Resend => sp.GetRequiredService<ResendEmailSender>(),
            _ => sp.GetRequiredService<SmtpEmailSender>(),
        });
        return services;
    }
}
