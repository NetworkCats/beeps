using Beeps.Features.Moderation;
using Beeps.Infrastructure.Outbox;
using Beeps.Infrastructure.Queues;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Beeps.Infrastructure.Moderation;

/// <summary>Reviews the beeps waiting for it (<see cref="BeepJobWorker"/>), woken as beeps are posted.</summary>
public sealed class ModerationWorker(
    IServiceScopeFactory scopes,
    IModerationJobStore store,
    NpgsqlDataSource dataSource,
    IOptions<ModerationOptions> options,
    TimeProvider time,
    ILogger<ModerationWorker> logger) : BeepJobWorker(scopes, store, dataSource, options.Value, options.Value.Enabled, time, logger)
{
    protected override string Queue => "Moderation";

    protected override string Channel => WorkSignals.ModerationChannel;

    protected override Task RunAsync(IServiceProvider services, ClaimedBeepJob job, CancellationToken stoppingToken) =>
        services.GetRequiredService<ModerationProcessor>().ProcessAsync(job, stoppingToken);
}
