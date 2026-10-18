using Beeps.Features.Shared;
using Beeps.Infrastructure.Data.Stores;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Caching;

public static class CacheSetup
{
    public static IServiceCollection AddBeepsContentCache(this IServiceCollection services)
    {
        services.AddOptions<CacheOptions>().BindConfiguration(CacheOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CacheOptions>, CacheOptionsValidator>();
        services.AddSingleton<ISharedContentCache, RedisSharedContentCache>();
        services.AddSingleton<ContentCache>();
        services.AddSingleton<CacheInvalidator>();
        services.AddHostedService(sp => sp.GetRequiredService<CacheInvalidator>());
        services.AddHostedService<CacheInvalidationListener>();
        services.AddScoped<PendingCacheChanges>();
        services.AddScoped<IOutboxHandler, CacheInvalidationHandler>();
        services.AddScoped<IOutboxHandler, SharedCacheForgetHandler>();
        services.AddScoped<ContentReads>();
        return services;
    }
}
