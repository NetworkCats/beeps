using Beeps.Infrastructure.Data.Stores;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

public static class RedisSetup
{
    public const string ConnectionStringName = "Redis";

    /// <summary>Every key this app writes starts with this, per PLAN 6.7: <c>beeps:v1:{type}:{id}</c>.</summary>
    public const string KeyPrefix = "beeps:v1:";

    public static IServiceCollection AddBeepsRedis(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            var options = ConfigurationOptions.Parse(connectionString);
            // Keep retrying in the background instead of failing startup when Valkey is briefly away.
            options.AbortOnConnectFail = false;
            options.ClientName = "beeps";
            return ConnectionMultiplexer.Connect(options);
        });

        // HybridCache: short-lived in-process L1 in front of the shared Valkey L2. L1 TTL stays short
        // because instances must not drift apart (PLAN 12, 无状态).
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<IServiceProvider>((options, sp) =>
        {
            options.InstanceName = $"{KeyPrefix}cache:";
            options.ConnectionMultiplexerFactory = () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>());
        });
        services.AddHybridCache(options => options.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(5),
            LocalCacheExpiration = TimeSpan.FromSeconds(30),
        }).AddBeepsCacheSerializers();
        return services;
    }
}
