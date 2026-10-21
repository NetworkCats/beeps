namespace Beeps.Features.RateLimiting;

/// <summary>
/// Selects the rate limit policy for an endpoint or Razor Page (as an attribute on the PageModel).
/// Endpoints without it use <see cref="RateLimitPolicies.AnonymousBrowse"/> or <see cref="RateLimitPolicies.Browse"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RateLimitPolicyAttribute(string policy) : Attribute
{
    public string Policy { get; } = policy;
}

/// <summary>
/// Exempts an endpoint from rate limiting. Only for static assets, which the CDN serves anyway, and the health check,
/// which nginx does not pass on.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipRateLimitAttribute : Attribute;

public static class RateLimitEndpointExtensions
{
    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, string policy)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitPolicyAttribute(policy));

    public static TBuilder WithoutRateLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SkipRateLimitAttribute());
}
