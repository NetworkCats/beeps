using Beeps.Features.Shared;

namespace Beeps.Infrastructure.Outbox;

public static class OutboxSetup
{
    public static IServiceCollection AddBeepsOutbox(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddSingleton<IOutboxStore, PostgresOutboxStore>();
        services.AddScoped<OutboxProcessor>();
        // One worker per queue. Not AddHostedService: it registers a type once, however many factories it is given.
        foreach (var queue in OutboxQueues.All)
        {
            services.AddSingleton<IHostedService>(provider => ActivatorUtilities.CreateInstance<OutboxWorker>(provider, queue));
        }

        return services;
    }
}
