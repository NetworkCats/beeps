using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Diagnostics;

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
        // An error page rendered for a request (re-executed) belongs to a request that was counted already.
        if (metadata?.GetMetadata<SkipRateLimitAttribute>() is not null
            || context.Features.Get<IStatusCodeReExecuteFeature>() is not null
            || context.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            await next(context);
            return;
        }

        var signedIn = context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not null;
        var policy = metadata?.GetMetadata<RateLimitPolicyAttribute>()?.Policy
            ?? (signedIn ? RateLimitPolicies.Browse : RateLimitPolicies.AnonymousBrowse);
        var partition = signedIn
            ? RateLimitPartitions.Account(context.User.ActorId())
            : RateLimitPartitions.Ip(context.Connection.RemoteIpAddress?.ToString());

        var result = await rateLimits.CheckAsync(policy, partition, context.RequestAborted);
        if (!result.IsSuccess)
        {
            if (AuthSetup.IsApi(context.Request))
            {
                await result.Error.ToHttpResult().ExecuteAsync(context);
            }
            else
            {
                // No body, so the status code pages render the localized HTML error page.
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = ErrorHttpResult.RetryAfterHeaderValue(result.Error.RetryAfter ?? TimeSpan.Zero);
            }

            return;
        }

        await next(context);
    }
}
