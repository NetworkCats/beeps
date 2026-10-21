using System.Data.Common;
using Beeps.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Beeps.Infrastructure.Data;

public static class DatabaseSetup
{
    public const string ConnectionStringName = "Postgres";

    /// <summary>Statements kept prepared per connection: more than the app's distinct hot queries.</summary>
    public const int MaxAutoPrepare = 200;

    /// <summary>Seconds an idle connection waits before checking that the server is still there.</summary>
    public const int KeepAlive = 30;

    private const string PlanCacheMode = "plan_cache_mode";

    public static IServiceCollection AddBeepsDatabase(this IServiceCollection services)
    {
        // One data source (connection pool) shared by EF Core and the raw-SQL users (the outbox, moderation jobs, scheduled jobs and idempotency stores).
        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            var builder = new NpgsqlDataSourceBuilder(connectionString);
            Prepare(builder.ConnectionStringBuilder);
            CheckIdleConnections(builder.ConnectionStringBuilder);
            return builder.Build();
        });
        services.AddDbContextPool<BeepsDbContext>((sp, options) =>
            Configure(options, sp.GetRequiredService<NpgsqlDataSource>()).AddInterceptors(sp.GetRequiredService<WorkSignalInterceptor>()));
        return services;
    }

    /// <summary>
    /// EF Core sends the same few statements over and over. Prepared, PostgreSQL parses and analyses each once per
    /// connection, and the pool's reset between requests is lighter (no DISCARD ALL): about 15% less database CPU per
    /// page. The connection string may still set any of these (Max Auto Prepare=0 turns it off).
    /// </summary>
    internal static void Prepare(NpgsqlConnectionStringBuilder settings)
    {
        var given = Given(settings);
        if (given.Contains(nameof(settings.MaxAutoPrepare)))
        {
            return;
        }

        settings.MaxAutoPrepare = MaxAutoPrepare;
        // From the first use: EF opens a connection per query, and the reset that follows clears what Npgsql counted
        // towards the default of 5, so nothing would ever reach it.
        if (!given.Contains(nameof(settings.AutoPrepareMinUsages)))
        {
            settings.AutoPrepareMinUsages = 1;
        }

        // Still planned for each run's own values: after five runs PostgreSQL may switch a prepared statement to one
        // generic plan, which for the inbox's list was ten times slower than planning it each time.
        if (settings.Options?.Contains(PlanCacheMode, StringComparison.Ordinal) != true)
        {
            settings.Options = $"{settings.Options} -c {PlanCacheMode}=force_custom_plan".TrimStart();
        }
    }

    /// <summary>
    /// The workers' LISTEN connections only ever wait. A socket a NAT or failover dropped without a word would leave that
    /// wait timing out quietly for good, every NOTIFY lost and the queues down to their poll interval; a keepalive query
    /// makes it fail, and the worker reconnects. The connection string may still set its own (Keepalive=0 turns it off).
    /// </summary>
    internal static void CheckIdleConnections(NpgsqlConnectionStringBuilder settings)
    {
        if (!Given(settings).Contains(nameof(settings.KeepAlive)))
        {
            settings.KeepAlive = KeepAlive;
        }
    }

    // ContainsKey says whether Npgsql knows a keyword, not whether it was given; keys are matched without their spaces.
    private static HashSet<string> Given(NpgsqlConnectionStringBuilder settings) =>
        new DbConnectionStringBuilder { ConnectionString = settings.ConnectionString }.Keys
            .Cast<string>()
            .Select(key => key.Replace(" ", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Some limits are caps on a count or a list of anyone (the unread count stops at 100), where any rows will do: an
    // ORDER BY there would read every row to sort them, so the warning about a limit without one is off.
    private static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(CoreEventId.RowLimitingOperationWithoutOrderByWarning));
}
