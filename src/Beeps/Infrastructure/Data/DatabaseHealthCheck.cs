using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Beeps.Infrastructure.Data;

/// <summary>
/// <c>/healthz</c>: the instance answers and reaches PostgreSQL, so a deploy can send it traffic. Valkey and
/// Meilisearch are left out: the app keeps working without them, and an instance should not be held back for them.
/// </summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public const string Path = "/healthz";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // A failure throws, and the health check service reports it as unhealthy.
        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
