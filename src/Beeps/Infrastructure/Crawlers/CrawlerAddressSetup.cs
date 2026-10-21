using Beeps.Features.Crawlers;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Crawlers;

public static class CrawlerAddressSetup
{
    public static IServiceCollection AddBeepsCrawlerAddresses(this IServiceCollection services)
    {
        services.AddOptions<CrawlerAddressOptions>().BindConfiguration(CrawlerAddressOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<CrawlerAddressList>();
        services.AddSingleton<ICrawlerAddresses>(sp => sp.GetRequiredService<CrawlerAddressList>());
        services.AddHostedService<CrawlerAddressUpdater>();
        services.AddHttpClient(CrawlerAddressUpdater.HttpClientName, (sp, client) =>
        {
            client.Timeout = sp.GetRequiredService<IOptions<CrawlerAddressOptions>>().Value.DownloadTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("beeps");
        });
        return services;
    }
}
