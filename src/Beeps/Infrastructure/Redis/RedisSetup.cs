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

    /// <summary>How long a command may take unless the connection string says otherwise.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long connecting may take unless the connection string says otherwise.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The client's settings: those in the connection string, and the defaults beeps needs where it has none.</summary>
    public static ConfigurationOptions Options(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        // Keep retrying in the background instead of failing startup when Valkey is briefly away.
        options.AbortOnConnectFail = false;
        // While disconnected, fail commands at once instead of queueing them until they time out (5 s each):
        // every caller already falls back (to PostgreSQL, or by letting the request through).
        options.BacklogPolicy = BacklogPolicy.FailFast;
        // That only helps once the connection is known to be gone. A Valkey that hangs instead (a stalled fork, a
        // network that drops packets) would hold every command for the 5 s default, one after another in a request,
        // before its fallback runs; commands take well under a millisecond, so give up far sooner.
        var given = connectionString.Split(',').Select(p => p.Split('=')[0].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!given.Contains("syncTimeout"))
        {
            options.SyncTimeout = (int)CommandTimeout.TotalMilliseconds;
        }

        if (!given.Contains("asyncTimeout"))
        {
            options.AsyncTimeout = (int)CommandTimeout.TotalMilliseconds;
        }

        if (!given.Contains("connectTimeout"))
        {
            options.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
        }

        // The first connection is waited for once; past that the client keeps reconnecting in the background
        // (AbortOnConnectFail), so retrying it here would only make whoever needs Valkey first wait three times as long.
        if (!given.Contains("connectRetry"))
        {
            options.ConnectRetry = 1;
        }

        options.ClientName = "beeps";
        return options;
    }

    public static IServiceCollection AddBeepsRedis(this IServiceCollection services)
    {
        services.AddSingleton<RedisConnection>();

        // HybridCache over Valkey. Every entry passes its own options (see SharedCache and EfSystemStore); the
        // ones that decide who a request is skip the in-process copy, because instances must not drift apart (PLAN 12).
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<IServiceProvider>((options, sp) =>
        {
            options.InstanceName = $"{KeyPrefix}cache:";
            options.ConnectionMultiplexerFactory = () => sp.GetRequiredService<RedisConnection>().GetAsync();
        });
        services.AddHybridCache().AddBeepsCacheSerializers();
        return services;
    }
}
