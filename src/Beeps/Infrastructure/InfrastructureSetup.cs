using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Captcha;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Data.Stores;
using Beeps.Infrastructure.Email;
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
        services.AddBeepsEmail();
        services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddScoped<IAgentStore, EfAgentStore>();
        services.AddScoped<IAdminStore, EfAdminStore>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));

        // Keys live in PostgreSQL, not Redis: Redis must stay rebuildable, and losing these keys
        // would sign everyone out and invalidate every antiforgery token.
        services.AddDataProtection()
            .SetApplicationName("beeps")
            .PersistKeysToDbContext<BeepsDbContext>();
        return services;
    }
}
