using Beeps.Features.Accounts;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.IpGeolocation;

public static class IpLocationSetup
{
    public static IServiceCollection AddBeepsIpLocation(this IServiceCollection services)
    {
        services.AddOptions<IpLocationOptions>().BindConfiguration(IpLocationOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<MaxMindIpLocations>();
        services.AddSingleton<IIpLocations>(sp => sp.GetRequiredService<MaxMindIpLocations>());
        services.AddHostedService<IpLocationUpdater>();
        services.AddHttpClient(IpLocationUpdater.HttpClientName, (sp, client) =>
        {
            client.Timeout = sp.GetRequiredService<IOptions<IpLocationOptions>>().Value.DownloadTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("beeps");
        });
        return services;
    }
}
