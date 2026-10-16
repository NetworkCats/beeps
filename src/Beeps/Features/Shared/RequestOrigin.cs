namespace Beeps.Features.Shared;

/// <summary>
/// Facts about the caller that Services need but must not read from HttpContext themselves: the client IP
/// (already resolved by ForwardedHeaders) for rate limits and Turnstile, and the page language.
/// </summary>
public sealed record RequestOrigin(string ClientIp, string Culture);
