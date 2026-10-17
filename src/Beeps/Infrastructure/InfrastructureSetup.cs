using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Infrastructure.Caching;
using Beeps.Infrastructure.Captcha;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Data.Stores;
using Beeps.Infrastructure.Email;
using Beeps.Infrastructure.Http;
using Beeps.Infrastructure.Jobs;
using Beeps.Infrastructure.Live;
using Beeps.Infrastructure.Media;
using Beeps.Infrastructure.Moderation;
using Beeps.Infrastructure.Outbox;
using Beeps.Infrastructure.Redis;
using Beeps.Infrastructure.Search;
using Beeps.Infrastructure.Storage;
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
        services.AddBeepsContentCache();
        services.AddBeepsOutbox();
        services.AddBeepsScheduledJobs();
        services.AddBeepsEmail();
        services.AddBeepsStorage();
        services.AddBeepsLive();
        services.AddBeepsSearch();
        services.AddBeepsModeration();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IBeepCounterStore, RedisBeepCounterStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddScoped<ISessionStore, EfSessionStore>();
        services.AddScoped<IAgentStore, EfAgentStore>();
        services.AddScoped<IAdminStore, EfAdminStore>();
        services.AddScoped<IBeepStore, EfBeepStore>();
        services.AddScoped<ITimelineStore, EfTimelineStore>();
        services.AddScoped<IProfileStore, EfProfileStore>();
        services.AddScoped<INotificationStore, EfNotificationStore>();
        services.AddScoped<IModerationStore, EfModerationStore>();
        services.AddScoped<ISystemStore, EfSystemStore>();
        services.AddScoped<EfMediaStore>();
        services.AddScoped<IMediaStore>(sp => sp.GetRequiredService<EfMediaStore>());
        services.AddScoped<IAvatarStore>(sp => sp.GetRequiredService<EfMediaStore>());
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));

        // Keys live in PostgreSQL, not Redis: Redis must stay rebuildable, and losing these keys
        // would sign everyone out and invalidate every antiforgery token.
        services.AddDataProtection()
            .SetApplicationName("beeps")
            .PersistKeysToDbContext<BeepsDbContext>();
        return services;
    }
}
