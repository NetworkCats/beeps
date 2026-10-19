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
    /// holds, counts an attempt on each and hides them until <paramref name="leaseUntil"/>. Oldest first.
    /// </summary>
    Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(OutboxTypes types, int limit, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    Task CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task RescheduleAsync(Guid id, DateTimeOffset runAfter, string error, CancellationToken cancellationToken);

    Task DeadLetterAsync(Guid id, string error, DateTimeOffset failedAt, CancellationToken cancellationToken);
}
