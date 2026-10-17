namespace Beeps.Infrastructure.Data;

/// <summary>A row of <c>idempotency_keys</c>: what <see cref="Stores.PostgresIdempotencyStore"/> keeps per key.</summary>
public sealed class IdempotencyEntry
{
    /// <summary>The caller and a hash of their key (<c>{actor}:{sha256}</c>).</summary>
    public const int KeyMaxLength = 100;

    public required string Key { get; init; }

    public required string Fingerprint { get; set; }

    /// <summary>Null while the first request is still running.</summary>
    public int? StatusCode { get; set; }

    public string? ContentType { get; set; }

    public string? Location { get; set; }

    public string? Body { get; set; }

    /// <summary>A claim lasts a minute, a response a day; after that the key may be used again.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
