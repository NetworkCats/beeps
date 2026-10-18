using System.Text.Json.Serialization;
using Beeps.Features.Shared;

namespace Beeps.Features.Profiles;

/// <summary>Outbox payload: a human was banned, unbanned or deleted, so everyone they follow or are followed by counts anew.</summary>
public sealed record FollowCountsRequest(Guid HumanId)
{
    public const string MessageType = "profiles.follow_counts";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(FollowCountsRequest))]
internal sealed partial class ProfilesJsonContext : JsonSerializerContext;

/// <summary>
/// Follow counts leave out people who are not shown (PLAN 5.1). A follow or an unfollow changes them as it goes; when
/// someone stops or starts being shown, the people on the other side of their follows are counted again, in the
/// background. Each recount reads the rows as they are then, so it is idempotent and the order does not matter.
/// </summary>
public static class FollowCountsSync
{
    /// <summary>In the same transaction as the ban, unban or deletion, which the recount must come after.</summary>
    public static void RecountFollows(this IOutbox outbox, Guid humanId)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        outbox.Enqueue(FollowCountsRequest.MessageType, new FollowCountsRequest(humanId), ProfilesJsonContext.Default.FollowCountsRequest);
    }
}

public sealed class FollowCountsHandler(IProfileStore store, IUnitOfWork unitOfWork) : IOutboxHandler
{
    /// <summary>Rows locked and counted per transaction, so a follow of someone in it waits at most for one batch.</summary>
    public const int BatchSize = 500;

    public string Type => FollowCountsRequest.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var humanId = message.ReadPayload(ProfilesJsonContext.Default.FollowCountsRequest).HumanId;
        var people = await store.ListFollowCounterpartsAsync(humanId, cancellationToken);
        // Sorted, so the batches lock in the order follows do.
        foreach (var batch in people.Append(humanId).Distinct().Order().Chunk(BatchSize))
        {
            await unitOfWork.InTransactionAsync(
                null,
                async ct =>
                {
                    await store.RecountFollowsAsync(batch, ct);
                    return Result.Success;
                },
                cancellationToken);
        }
    }
}
