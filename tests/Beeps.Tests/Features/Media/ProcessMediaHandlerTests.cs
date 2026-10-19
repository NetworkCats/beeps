using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Media;

/// <summary>The outbox job that decodes accepted uploads, through the real SkiaSharp processor; storage and store are mocks.</summary>
public sealed class ProcessMediaHandlerTests
{
    private static readonly Viewer Agent = Viewer.Agent(Guid.NewGuid(), Guid.NewGuid());

    private readonly MediaServiceParts parts = MediaServiceParts.WithRealImages();

    [Fact]
    public async Task EverySizeIsStoredAsWebPAndTheImageBecomesReady()
    {
        var media = await UploadAsync(TestImages.Quadrants(3000, 1000));

        await parts.ProcessQueuedAsync();

        foreach (var name in new[] { MediaObjects.Full(media.StorageKey), MediaObjects.Thumbnail(media.StorageKey), MediaObjects.Avatar(media.StorageKey) })
        {
            await parts.Storage.Received(1).PutAsync(name, Arg.Is<ReadOnlyMemory<byte>>(m => IsWebP(m)), "image/webp", Arg.Any<CancellationToken>());
        }

        // Only uploads from a banner form are stored in a banner size.
        await parts.Storage.DidNotReceive().PutAsync(MediaObjects.Banner(media.StorageKey), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await parts.Store.Received(1).FinishProcessingAsync(media.Id, MediaStatus.Ready, 2048, 683, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatDoesNotDecodeFailsForGoodWithoutARetry()
    {
        var jpeg = TestImages.Quadrants(400, 200, SkiaSharp.SKEncodedImageFormat.Jpeg);
        // The header is intact, so the upload itself is accepted.
        var media = await UploadAsync(jpeg[..(jpeg.Length / 2)]);

        await parts.ProcessQueuedAsync();

        await parts.Store.Received(1).FinishProcessingAsync(media.Id, MediaStatus.Failed, media.Width, media.Height, Arg.Any<CancellationToken>());
        await parts.Storage.DidNotReceiveWithAnyArgs().PutAsync(default!, default, default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AStorageFailureThrowsSoTheOutboxRetriesIt()
    {
        var media = await UploadAsync(TestImages.Png());
        var message = parts.Queued().Single();
        parts.Storage.PutAsync(default!, default, default!, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException(new HttpRequestException("storage down")), Task.CompletedTask);
        var handler = parts.CreateProcessor();

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.HandleAsync(message, TestSupport.Ct));
        await parts.Store.DidNotReceiveWithAnyArgs().FinishProcessingAsync(default, default, default, default, TestSupport.Ct);

        await handler.HandleAsync(message, TestSupport.Ct);
        await parts.Store.Received(1).FinishProcessingAsync(media.Id, MediaStatus.Ready, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepeatedMessageDoesNothing()
    {
        await UploadAsync(TestImages.Png());
        var message = parts.Queued().Single();
        var handler = parts.CreateProcessor();

        await handler.HandleAsync(message, TestSupport.Ct);
        await handler.HandleAsync(message, TestSupport.Ct);

        await parts.Storage.ReceivedWithAnyArgs(3).PutAsync(default!, default, default!, TestSupport.Ct);
        await parts.Store.ReceivedWithAnyArgs(1).FinishProcessingAsync(default, default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ObjectsWrittenForARowThatWentMeanwhileAreRemoved()
    {
        var media = await UploadAsync(TestImages.Png());
        parts.Store.FinishProcessingAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(MediaFinish.Gone);

        await parts.ProcessQueuedAsync();

        await parts.Storage.Received(1).DeleteAsync(Arg.Is<IReadOnlyCollection<string>>(n => n.SequenceEqual(MediaObjects.All(media.StorageKey))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ObjectsOfAnImageAnEarlierDeliveryMadeReadyAreKept()
    {
        await UploadAsync(TestImages.Png());
        parts.Store.FinishProcessingAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(MediaFinish.AlreadyReady);

        await parts.ProcessQueuedAsync();

        await parts.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AnAvatarSetWhileProcessingIsRefreshedOnceReady()
    {
        var media = await UploadAsync(TestImages.Png());
        var actorId = Guid.NewGuid();
        parts.Store.ListActorsUsingAsync(media.Id, Arg.Any<CancellationToken>()).Returns([actorId]);

        await parts.ProcessQueuedAsync();

        await parts.Accounts.Received(1).ForgetAccountAsync(actorId, Arg.Any<CancellationToken>());
        Assert.Equal([$$"""{"actor_id":"{{actorId:D}}"}"""], TestData.LiveEvents(parts.LiveEvents, LiveEventTypes.ActorChanged));
    }

    [Fact]
    public async Task BeepsPostedWhileTheirImageProcessedAreRedrawnOnceReady()
    {
        var media = await UploadAsync(TestImages.Png());
        var beepId = Guid.NewGuid();
        parts.Store.ListBeepsUsingAsync(media.Id, Arg.Any<CancellationToken>()).Returns([beepId]);

        await parts.ProcessQueuedAsync();

        Assert.Equal([$$"""{"ids":["{{beepId:D}}"]}"""], TestData.LiveEvents(parts.LiveEvents, LiveEventTypes.BeepsChanged));
    }

    [Fact]
    public async Task AnImageThatCannotBeReadChangesNoBeep()
    {
        var media = await UploadAsync(TestImages.Png());
        parts.Store.ListBeepsUsingAsync(media.Id, Arg.Any<CancellationToken>()).Returns([Guid.NewGuid()]);
        parts.Store.FinishProcessingAsync(default, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(MediaFinish.Gone);

        await parts.ProcessQueuedAsync();

        Assert.Empty(TestData.LiveEvents(parts.LiveEvents, LiveEventTypes.BeepsChanged));
    }

    private async Task<MediaItem> UploadAsync(byte[] data)
    {
        MediaItem? stored = null;
        parts.Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => stored = ci.Arg<MediaItem>());
        var result = await parts.Create().UploadAsync(Agent, TestImages.Content(data), TestSupport.Ct);
        Assert.True(result.IsSuccess, result.Error?.Code);
        return stored!;
    }

    private static bool IsWebP(ReadOnlyMemory<byte> data) => MediaRules.Sniff(data.Span) == ImageFormat.WebP;
}
