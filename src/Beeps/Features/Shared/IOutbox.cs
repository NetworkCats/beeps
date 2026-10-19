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

/// <summary>
/// The outbox's queues. Each has its own worker on every instance, so a backlog in one (a slow or stalled service its
/// handlers depend on) holds up nothing in another.
/// </summary>
public static class OutboxQueues
{
    /// <summary>Notifications, emails, media, counts, cache invalidations: everything without a queue of its own.</summary>
    public const string Main = "main";

    /// <summary>Search index sync, which waits on Meilisearch.</summary>
    public const string Search = "search";

    public static readonly IReadOnlyList<string> All = [Main, Search];
}

/// <summary>Runs one message type. Must be idempotent: a message can be delivered more than once.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    /// <summary>The queue its messages run in (<see cref="OutboxQueues"/>).</summary>
    string Queue => OutboxQueues.Main;

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
