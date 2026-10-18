using StackExchange.Redis;

namespace Beeps.Infrastructure.Redis;

/// <summary>
/// The one connection to Valkey, made on first use and waited for asynchronously. Connecting synchronously (in a service
/// factory) held a thread, and the service provider with it, for up to the connect timeout whenever Valkey hung, which
/// startup and the first requests then waited for.
/// </summary>
public sealed class RedisConnection(IConfiguration configuration) : IAsyncDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> connection = new(() => ConnectAsync(configuration));

    /// <summary>The connection; while Valkey is away, one that fails commands at once and keeps reconnecting.</summary>
    public Task<IConnectionMultiplexer> GetAsync() => connection.Value;

    public async Task<IDatabase> DatabaseAsync() => (await GetAsync()).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (connection.IsValueCreated && connection.Value.IsCompletedSuccessfully)
        {
            await connection.Value.Result.DisposeAsync();
        }
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RedisSetup.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"ConnectionStrings:{RedisSetup.ConnectionStringName} is not configured.");
        }

        return await ConnectionMultiplexer.ConnectAsync(RedisSetup.Options(connectionString));
    }
}
