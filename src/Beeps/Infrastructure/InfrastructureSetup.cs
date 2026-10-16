using Beeps.Features.RateLimiting;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Http;
using Beeps.Infrastructure.Outbox;
using Beeps.Infrastructure.Redis;
using Microsoft.AspNetCore.DataProtection;

namespace Beeps.Infrastructure;

public static class InfrastructureSetup
{
    public static IServiceCollection AddBeepsInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddBeepsForwardedHeaders();
        services.AddBeepsDatabase();
        services.AddBeepsRedis();
        services.AddBeepsOutbox();
        services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();

        // Keys live in PostgreSQL, not Redis: Redis must stay rebuildable, and losing these keys
        // would sign everyone out and invalidate every antiforgery token.
        services.AddDataProtection()
            .SetApplicationName("beeps")
            .PersistKeysToDbContext<BeepsDbContext>();
        return services;
    }
}
