using System.Security.Claims;
using Beeps.Features.Shared.Http;

namespace Beeps.Features.RateLimiting;

/// <summary>
/// Applies a rate limit to every request: the endpoint's own policy, or the browse default.
/// Signed-in callers are counted per account, everyone else per client IP (which only
/// ForwardedHeaders may set, from a trusted proxy).
/// </summary>
public sealed class RateLimitingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RateLimitService rateLimits)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rateLimits);
        var metadata = context.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<SkipRateLimitAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var policy = metadata?.GetMetadata<RateLimitPolicyAttribute>()?.Policy
            ?? (accountId is null ? RateLimitPolicies.AnonymousBrowse : RateLimitPolicies.Browse);
        var partition = accountId is null ? $"ip:{context.Connection.RemoteIpAddress}" : $"account:{accountId}";

        var result = await rateLimits.CheckAsync(policy, partition, context.RequestAborted);
        if (!result.IsSuccess)
        {
            await result.Error.ToHttpResult().ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
