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
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}
