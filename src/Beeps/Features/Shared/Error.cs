namespace Beeps.Features.Shared;

/// <summary>The category of a failure; decides the HTTP status code at the edge.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RateLimited,

    /// <summary>The request carried more data than allowed (an oversized upload).</summary>
    TooLarge,
}

/// <summary>
/// A business failure returned by a Service. <see cref="Code"/> is a stable English identifier
/// (snake_case) that clients can rely on; <see cref="Detail"/> is human-readable and may be localized.
/// </summary>
public sealed record Error(ErrorKind Kind, string Code, string? Detail = null)
{
    /// <summary>Per-field messages for <see cref="ErrorKind.Validation"/>, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }

    /// <summary>For <see cref="ErrorKind.RateLimited"/>: how long the caller has to wait.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static Error Validation(string code, string? detail = null, IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(ErrorKind.Validation, code, detail) { Fields = fields };

    public static Error Unauthorized(string code, string? detail = null) => new(ErrorKind.Unauthorized, code, detail);

    public static Error Forbidden(string code, string? detail = null) => new(ErrorKind.Forbidden, code, detail);

    public static Error NotFound(string code, string? detail = null) => new(ErrorKind.NotFound, code, detail);

    public static Error Conflict(string code, string? detail = null) => new(ErrorKind.Conflict, code, detail);

    public static Error RateLimited(TimeSpan retryAfter, string? detail = null) =>
        new(ErrorKind.RateLimited, ErrorCodes.RateLimited, detail) { RetryAfter = retryAfter };
}
