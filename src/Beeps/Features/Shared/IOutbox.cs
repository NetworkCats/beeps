using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Beeps.Features.Shared;

/// <summary>
/// Queues follow-up work (notifications, emails, index sync…) in the same transaction as the business
/// change that caused it: the message is staged on the current unit of work and committed with it.
/// </summary>
public interface IOutbox
{
    /// <param name="type">Stable message type, matched to an <see cref="IOutboxHandler.Type"/>.</param>
    void Enqueue<T>(string type, T payload, JsonTypeInfo<T> payloadType);
}

/// <summary>Runs one message type. Must be idempotent: a message can be delivered more than once.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    /// <summary>Throw to have the message retried with backoff (and dead-lettered after too many attempts).</summary>
    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}

/// <param name="Attempts">Includes the current attempt.</param>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, int Attempts, DateTimeOffset CreatedAt)
{
    public T ReadPayload<T>(JsonTypeInfo<T> payloadType) =>
        JsonSerializer.Deserialize(Payload, payloadType)
        ?? throw new JsonException($"Outbox message {Id} ({Type}) has an empty payload.");
}
