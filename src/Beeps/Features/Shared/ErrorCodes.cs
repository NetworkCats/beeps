namespace Beeps.Features.Shared;

/// <summary>Error codes shared across features. Published codes must never change.</summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string TooLarge = "too_large";
    public const string InternalError = "internal_error";
    public const string Unavailable = "unavailable";

    /// <summary>The code used when a response is produced without an explicit one (framework 404s, crashes…).</summary>
    public static string ForStatus(int statusCode) => statusCode switch
    {
        400 => BadRequest,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        413 => TooLarge,
        429 => RateLimited,
        503 => Unavailable,
        >= 500 => InternalError,
        _ => BadRequest,
    };
}
