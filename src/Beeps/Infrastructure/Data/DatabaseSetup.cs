using Microsoft.EntityFrameworkCore;
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

    public static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
