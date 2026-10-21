using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Support;

/// <summary>
/// A real <see cref="MediaService"/> over mocks, as other Services' tests need one. The store mock remembers what
/// was uploaded, so <see cref="ProcessQueuedAsync"/> can run the queued processing as the outbox worker would.
/// </summary>
internal sealed class MediaServiceParts
{
    private readonly Dictionary<Guid, MediaItem> media = [];
    private readonly Dictionary<Guid, MediaUpload> uploads = [];
    private int processed;

    public IImageProcessor Images { get; init; } = Substitute.For<IImageProcessor>();

    public IMediaStorage Storage { get; } = Substitute.For<IMediaStorage>();

    public IMediaStore Store { get; } = Substitute.For<IMediaStore>();

    public IOutbox Outbox { get; } = Substitute.For<IOutbox>();

    public IRateLimitStore RateLimits { get; } = Substitute.For<IRateLimitStore>();

    public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();

    public ILiveEventStore LiveEvents { get; } = Substitute.For<ILiveEventStore>();

    /// <summary>The beeps an image is posted with, for a processor that deletes a beep left with nothing in it.</summary>
    public IBeepStore Beeps { get; } = Substitute.For<IBeepStore>();

    public ImmediateUnitOfWork UnitOfWork { get; init; } = new();

    public MediaServiceParts()
    {
        RateLimits.TryAcquireAsync(default!, CancellationToken.None).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        Store.When(s => s.AddMedia(Arg.Any<MediaItem>())).Do(ci => media[ci.Arg<MediaItem>().Id] = ci.Arg<MediaItem>());
        Store.When(s => s.AddUpload(Arg.Any<MediaUpload>())).Do(ci => uploads[ci.Arg<MediaUpload>().MediaId] = ci.Arg<MediaUpload>());
        Store.FindUploadAsync(default, CancellationToken.None).ReturnsForAnyArgs(ci =>
            uploads.TryGetValue(ci.Arg<Guid>(), out var upload) && media.TryGetValue(upload.MediaId, out var item)
                ? new MediaUploadJob(item.StorageKey, item.Purpose, item.Width, item.Height, upload.Data, upload.Crop, upload.BannerCrop)
                : null);
        Store.FinishProcessingAsync(default, default, default, default, CancellationToken.None).ReturnsForAnyArgs(ci => uploads.Remove(ci.Arg<Guid>()) ? MediaFinish.Finished : MediaFinish.Gone);
        Store.ListActorsUsingAsync(default, CancellationToken.None).ReturnsForAnyArgs([]);
    }

    /// <summary>Locks exactly the ids it is asked to lock: every image belongs to the caller.</summary>
    public void OwnEverything() => OwnEverything(Store);

    /// <inheritdoc cref="OwnEverything()"/>
    public static void OwnEverything(IMediaStore store) =>
        store.LockOwnedAsync(default, default!, default, CancellationToken.None)
            .ReturnsForAnyArgs(ci => ci.Arg<IReadOnlyCollection<Guid>>() is { } ids ? (IReadOnlyList<Guid>)[.. ids] : []);

    public MediaService Create() => new(
        Images,
        Store,
        UnitOfWork,
        Outbox,
        TestData.Media,
        TestData.RateLimits(RateLimits),
        TestData.Time(),
        new TestLocalizer(),
        NullLogger<MediaService>.Instance);

    public ProcessMediaHandler CreateProcessor() => new(
        Store,
        Images,
        Storage,
        UnitOfWork,
        Accounts,
        CreateBeepService(),
        TestData.Live(LiveEvents),
        NullLogger<ProcessMediaHandler>.Instance);

    private BeepService CreateBeepService()
    {
        var counters = Substitute.For<IBeepCounterStore>();
        return new BeepService(
            Beeps,
            counters,
            Store,
            Create(),
            TestServices.Views(Beeps, counters),
            UnitOfWork,
            Outbox,
            Substitute.For<IModerationJobs>(),
            TestData.RateLimits(RateLimits),
            TestData.Live(LiveEvents, counters),
            TestData.Inboxes(LiveEvents, Substitute.For<INotificationStore>()),
            Options.Create(new ModerationOptions()),
            TestData.Time(),
            new TestLocalizer(),
            NullLogger<BeepService>.Instance);
    }

    /// <summary>The processing messages queued so far, as the outbox would deliver them.</summary>
    public IReadOnlyList<OutboxEnvelope> Queued() =>
        [.. Outbox.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IOutbox.Enqueue) && (string)c.GetArguments()[0]! == ProcessMediaRequest.MessageType)
            .Select(c => new OutboxEnvelope(
                Guid.NewGuid(),
                ProcessMediaRequest.MessageType,
                JsonSerializer.Serialize(c.GetArguments()[1], (JsonTypeInfo)c.GetArguments()[2]!),
                1,
                DateTimeOffset.UnixEpoch))];

    /// <summary>Runs the processing messages queued since the last call.</summary>
    public async Task ProcessQueuedAsync()
    {
        var queued = Queued();
        var handler = CreateProcessor();
        foreach (var message in queued.Skip(processed))
        {
            await handler.HandleAsync(message, TestSupport.Ct);
        }

        processed = queued.Count;
    }

    /// <summary>The real Skia processor: uploads in these tests go through actual decoding.</summary>
    public static MediaServiceParts WithRealImages(ImmediateUnitOfWork? unitOfWork = null) =>
        new() { Images = new SkiaImageProcessor(), UnitOfWork = unitOfWork ?? new ImmediateUnitOfWork() };
}
