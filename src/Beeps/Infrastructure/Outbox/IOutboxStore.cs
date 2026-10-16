using Beeps.Features.Shared;

namespace Beeps.Infrastructure.Outbox;

/// <summary>Queue operations used by the worker, kept behind an interface so the retry logic is testable.</summary>
public interface IOutboxStore
{
    /// <summary>
    /// Takes up to <paramref name="limit"/> due messages that no other worker holds, counts an attempt on
    /// each and hides them until <paramref name="leaseUntil"/>. Oldest first.
    /// </summary>
    Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    Task CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken);

    Task DeadLetterAsync(Guid id, string error, DateTimeOffset failedAt, CancellationToken cancellationToken);
}
