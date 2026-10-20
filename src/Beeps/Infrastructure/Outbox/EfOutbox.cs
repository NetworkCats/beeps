using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Shared;
using Beeps.Infrastructure.Data;

namespace Beeps.Infrastructure.Outbox;

/// <summary>
/// Stages messages on the request's DbContext, so the next <c>SaveChangesAsync</c> commits them in the
/// same transaction as the business data. The workers are woken once it commits (<see cref="WorkSignalInterceptor"/>).
/// </summary>
public sealed class EfOutbox(BeepsDbContext db, TimeProvider time) : IOutbox
{
    public void Enqueue<T>(string type, T payload, JsonTypeInfo<T> payloadType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type.Length, OutboxMessage.TypeMaxLength, nameof(type));
        var now = time.GetUtcNow();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Payload = JsonSerializer.Serialize(payload, payloadType),
            RunAfter = now,
            CreatedAt = now,
        });
    }
}
