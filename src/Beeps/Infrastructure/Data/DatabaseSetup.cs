using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Beeps.Infrastructure.Data;

public static class DatabaseSetup
{
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddBeepsDatabase(this IServiceCollection services)
    {
        // One data source (connection pool) shared by EF Core and the raw-SQL users (Outbox worker).
        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            return new NpgsqlDataSourceBuilder(connectionString).Build();
        });
        services.AddDbContextPool<BeepsDbContext>((sp, options) => Configure(options, sp.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    // Some limits are caps on a count or a list of anyone (the unread count stops at 100), where any rows will do: an
    // ORDER BY there would read every row to sort them, so the warning about a limit without one is off.
    public static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(CoreEventId.RowLimitingOperationWithoutOrderByWarning));
}
