using Beeps.Features.Live;

namespace Beeps.Infrastructure.Live;

public static class LiveSetup
{
    public static IServiceCollection AddBeepsLive(this IServiceCollection services)
    {
        services.AddSingleton<ILiveEventStore, RedisLiveEventStore>();
        services.AddSingleton<LiveEventHub>();
        services.AddSingleton<ILiveEventFeed>(sp => sp.GetRequiredService<LiveEventHub>());
        services.AddHostedService(sp => sp.GetRequiredService<LiveEventHub>());
        services.AddHostedService<LiveCountsWorker>();
        services.AddHostedService<AdminFiguresWorker>();
        return services;
    }
}
