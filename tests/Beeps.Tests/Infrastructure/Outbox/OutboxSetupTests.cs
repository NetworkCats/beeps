using Beeps.Features.Shared;
using Beeps.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Beeps.Tests.Infrastructure.Outbox;

public sealed class OutboxSetupTests
{
    [Fact]
    public async Task EveryQueueHasAWorker()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        // Not connected to until a worker runs.
        services.AddSingleton(NpgsqlDataSource.Create("Host=localhost"));
        services.AddBeepsOutbox();
        await using var provider = services.BuildServiceProvider();

        var queues = provider.GetServices<IHostedService>().OfType<OutboxWorker>().Select(worker => worker.Queue);

        Assert.Equal(OutboxQueues.All, queues);
    }
}
