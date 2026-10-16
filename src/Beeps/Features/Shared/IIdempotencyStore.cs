namespace Beeps.Features.Shared;

/// <summary>
/// Remembers write requests sent with an <c>Idempotency-Key</c> (PLAN 12) and what they answered, so a
/// retry gets the same answer instead of doing the write twice. Kept in Valkey: losing it only means a
/// retry after the loss runs again.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims the key for a request with this fingerprint. Returns null when the claim succeeded (first
    /// use); otherwise returns what is stored: a pending claim or a finished response.
    /// </summary>
    Task<IdempotencyRecord?> TryClaimAsync(string key, string fingerprint, TimeSpan claimDuration, CancellationToken cancellationToken);

    /// <summary>Replaces the claim with the response.</summary>
    Task CompleteAsync(string key, IdempotencyRecord record, TimeSpan retention, CancellationToken cancellationToken);

    /// <summary>Drops the claim, so the request can be retried (it failed or was not worth remembering).</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}

/// <param name="Fingerprint">Hash of the method, path and body: one key must not be reused for another request.</param>
/// <param name="StatusCode">Null while the first request is still running.</param>
public sealed record IdempotencyRecord(string Fingerprint, int? StatusCode, string? ContentType, string? Location, string? Body)
{
    public bool IsPending => StatusCode is null;
}
