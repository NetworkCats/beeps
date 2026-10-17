using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Shared;

public sealed class IdempotencyCleanupJobTests
{
    private readonly IIdempotencyStore store = Substitute.For<IIdempotencyStore>();

    [Fact]
    public async Task DeletesExpiredKeysWhileBatchesAreFull()
    {
        store.DeleteExpiredAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(IdempotencyCleanupJob.BatchSize, 3);

        await CreateJob().RunAsync(TestSupport.Ct);

        await store.Received(2).DeleteExpiredAsync(TestData.Now, IdempotencyCleanupJob.BatchSize, Arg.Any<CancellationToken>());
    }

    private IdempotencyCleanupJob CreateJob() => new(store, TestData.Time(), NullLogger<IdempotencyCleanupJob>.Instance);
}
