using System.Text.Json.Serialization;
using Beeps.Features.Shared;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Media;

/// <summary>Removes the stored objects of deleted media; an outbox message, so a storage outage only delays it.</summary>
public sealed record DeleteMediaObjectsRequest(IReadOnlyList<string> StorageKeys)
{
    public const string MessageType = "media.delete_objects";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DeleteMediaObjectsRequest))]
[JsonSerializable(typeof(ProcessMediaRequest))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;

/// <summary>
/// Deletes uploads that nothing uses once <see cref="MediaOptions.UnusedRetention"/> has passed: images
/// uploaded but never posted, avatars and banners that were replaced. Rows go first, in the same transaction as the
/// message that deletes their objects, so storage never keeps objects of rows that are gone for long.
/// </summary>
public sealed partial class MediaCleanupJob(
    IMediaStore store,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    IOptions<MediaOptions> options,
    TimeProvider time,
    ILogger<MediaCleanupJob> logger) : IScheduledJob
{
    public const string JobName = "media.cleanup";

    /// <summary>One message per batch: small enough for one storage call (S3 deletes up to 1000 objects at once).</summary>
    public const int BatchSize = 100;

    public string Name => JobName;

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.UnusedRetention;
        var total = 0;
        MediaPosition? after = null;
        int deleted;
        do
        {
            // Each batch goes on from the last: the media in use before it (almost all, the older they are) are read once a run.
            var batch = await unitOfWork.InTransactionAsync<UnusedMediaBatch>(
                null,
                async ct =>
                {
                    var found = await store.DeleteUnusedAsync(cutoff, after, BatchSize, ct);
                    if (found.StorageKeys.Count > 0)
                    {
                        outbox.Enqueue(DeleteMediaObjectsRequest.MessageType, new DeleteMediaObjectsRequest(found.StorageKeys), MediaJsonContext.Default.DeleteMediaObjectsRequest);
                        await unitOfWork.SaveChangesAsync(ct);
                    }

                    return Result.Of(found);
                },
                cancellationToken);
            deleted = batch.Value.StorageKeys.Count;
            after = batch.Value.Last;
            total += deleted;
        }
        while (deleted >= BatchSize);

        LogCleaned(logger, total);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Media cleanup deleted {Count} unused uploads")]
    private static partial void LogCleaned(ILogger logger, int count);
}

/// <summary>Deleting objects that are already gone succeeds, so a repeated message is harmless.</summary>
public sealed class DeleteMediaObjectsHandler(IMediaStorage storage) : IOutboxHandler
{
    public string Type => DeleteMediaObjectsRequest.MessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.ReadPayload(MediaJsonContext.Default.DeleteMediaObjectsRequest);
        return storage.DeleteAsync([.. request.StorageKeys.SelectMany(MediaObjects.All)], cancellationToken);
    }
}
