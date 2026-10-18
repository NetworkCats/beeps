using Beeps.Features.Shared;
using Microsoft.Extensions.Caching.Hybrid;

namespace Beeps.Infrastructure.Caching;

/// <summary>
/// The outbox's copy of a commit's invalidations (<see cref="PendingCacheChanges"/>): runs them once more, however the
/// in-process run went. That run is usually done by now; this one is what still happens if the instance stopped first.
/// </summary>
public sealed class CacheInvalidationHandler(CacheInvalidator invalidator) : IOutboxHandler
{
    public string Type => CacheInvalidationRequest.MessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return invalidator.RunAsync(message.ReadPayload(ContentCacheJsonContext.Default.CacheInvalidationRequest).Changes, cancellationToken);
    }
}

/// <summary>
/// The stored retry of sign-in entries (<see cref="Data.Stores.SharedCache"/>) whose removal failed after their change
/// committed (<see cref="CacheInvalidator.ForgetSharedAsync"/>). Throws while Valkey is still away, so it is retried.
/// </summary>
public sealed class SharedCacheForgetHandler(HybridCache hybrid) : IOutboxHandler
{
    public string Type => SharedCacheForgetRequest.MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await hybrid.RemoveAsync(message.ReadPayload(ContentCacheJsonContext.Default.SharedCacheForgetRequest).Keys, cancellationToken);
    }
}
