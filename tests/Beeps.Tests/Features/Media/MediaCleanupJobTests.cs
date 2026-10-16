using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Features.Media;

public sealed class MediaCleanupJobTests
{
    private static readonly string[] AllSizesOfAAndB = ["a/full.webp", "a/thumb.webp", "a/avatar.webp", "b/full.webp", "b/thumb.webp", "b/avatar.webp"];

    private readonly IMediaStore store = Substitute.For<IMediaStore>();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ImmediateUnitOfWork unitOfWork = new();

    [Fact]
    public async Task UnusedUploadsOlderThanTheRetentionAreDeletedWithAMessageToRemoveTheirObjects()
    {
        var full = Enumerable.Range(0, MediaCleanupJob.BatchSize).Select(i => $"key{i}").ToList();
        store.DeleteUnusedAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs(full, ["last"]);

        await Create().RunAsync(TestSupport.Ct);

        await store.Received(2).DeleteUnusedAsync(TestData.Now - TimeSpan.FromDays(1), MediaCleanupJob.BatchSize, Arg.Any<CancellationToken>());
        outbox.Received(1).Enqueue(DeleteMediaObjectsRequest.MessageType, Arg.Is<DeleteMediaObjectsRequest>(r => r.StorageKeys.Count == MediaCleanupJob.BatchSize), Arg.Any<JsonTypeInfo<DeleteMediaObjectsRequest>>());
        outbox.Received(1).Enqueue(DeleteMediaObjectsRequest.MessageType, Arg.Is<DeleteMediaObjectsRequest>(r => r.StorageKeys.Single() == "last"), Arg.Any<JsonTypeInfo<DeleteMediaObjectsRequest>>());
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task NothingToDeleteSendsNothing()
    {
        store.DeleteUnusedAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs([]);

        await Create().RunAsync(TestSupport.Ct);

        outbox.DidNotReceiveWithAnyArgs().Enqueue<DeleteMediaObjectsRequest>(default!, default!, default!);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task TheHandlerDeletesEverySizeOfEveryKey()
    {
        var storage = Substitute.For<IMediaStorage>();
        var payload = System.Text.Json.JsonSerializer.Serialize(new DeleteMediaObjectsRequest(["a", "b"]), MediaJsonContext.Default.DeleteMediaObjectsRequest);

        await new DeleteMediaObjectsHandler(storage).HandleAsync(new OutboxEnvelope(Guid.NewGuid(), DeleteMediaObjectsRequest.MessageType, payload, 1, TestData.Now), TestSupport.Ct);

        await storage.Received(1).DeleteAsync(
            Arg.Is<IReadOnlyCollection<string>>(names => names.SequenceEqual(AllSizesOfAAndB)),
            Arg.Any<CancellationToken>());
    }

    private MediaCleanupJob Create() =>
        new(store, unitOfWork, outbox, TestData.MediaOptions, TestData.Time(), NullLogger<MediaCleanupJob>.Instance);
}
