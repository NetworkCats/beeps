using Beeps.Features.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace Beeps.Features.Avatars;

public static class AvatarEndpoints
{
    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(DefaultAvatar.RoutePattern, GetDefault)
            .WithRateLimit(RateLimitPolicies.Avatar)
            .ExcludeFromDescription();
        return routes;
    }

    private static ContentHttpResult GetDefault(Guid id, HttpResponse response)
    {
        // Same id, same picture, forever: let browsers and Cloudflare keep it.
        response.Headers.CacheControl = "public, max-age=31536000, immutable";
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return TypedResults.Content(DefaultAvatar.RenderSvg(id), "image/svg+xml; charset=utf-8");
    }
}
