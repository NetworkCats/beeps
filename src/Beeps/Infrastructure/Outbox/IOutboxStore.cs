using Beeps.Features.Shared;

namespace Beeps.Infrastructure.Outbox;

/// <summary>
/// The message types a queue's worker takes: <see cref="Types"/>, or, with <see cref="Except"/>, every type but those
/// (the main queue takes what no other queue does, a type without a handler too, which it dead-letters).
/// </summary>
public sealed record OutboxTypes(IReadOnlyList<string> Types, bool Except);

/// <summary>Queue operations used by the worker, kept behind an interface so the retry logic is testable.</summary>
public interface IOutboxStore
{
    /// <summary>
    /// Takes up to <paramref name="limit"/> due messages of a queue's <paramref name="types"/> that no other worker
    /// holds and hides them until <paramref name="leaseUntil"/>. Oldest first. No attempt is counted yet
    /// (<see cref="StartAttemptAsync"/>): one that kills the process must not use up the attempts of the rest.
    /// </summary>
    Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(OutboxTypes types, int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>
    /// Counts an attempt on a claimed message just before it runs, and leases it until <paramref name="leaseUntil"/>:
    /// its attempts now, or null if it is gone or no longer held under <paramref name="claimedUntil"/> (claimed again
    /// elsewhere once that lease ran out).
    /// </summary>
    Task<int?> StartAttemptAsync(Guid id, DateTimeOffset claimedUntil, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>Keeps a message that is still running hidden until <paramref name="leaseUntil"/>.</summary>
    Task ExtendLeaseAsync(Guid id, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    Task CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken);

    Task DeadLetterAsync(Guid id, string error, DateTimeOffset failedAt, CancellationToken cancellationToken);
}
