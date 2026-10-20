using Beeps.Features.Accounts;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.DisposableEmails;

public static class DisposableEmailSetup
{
    public static IServiceCollection AddBeepsDisposableEmails(this IServiceCollection services)
    {
        services.AddOptions<DisposableEmailOptions>().BindConfiguration(DisposableEmailOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<DisposableEmailDomainList>();
        services.AddSingleton<IDisposableEmailDomains>(sp => sp.GetRequiredService<DisposableEmailDomainList>());
        services.AddHostedService<DisposableEmailUpdater>();
        services.AddHttpClient(DisposableEmailUpdater.HttpClientName, (sp, client) =>
        {
            client.Timeout = sp.GetRequiredService<IOptions<DisposableEmailOptions>>().Value.DownloadTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("beeps");
        });
        return services;
    }
}
