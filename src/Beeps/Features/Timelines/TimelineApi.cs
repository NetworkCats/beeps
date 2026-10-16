using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Beeps.Features.Timelines;

public static class TimelineApi
{
    public static IEndpointRouteBuilder MapTimelineApi(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1")
            .RequireAuthorization(AuthPolicies.ConnectedAgent)
            .WithRateLimit(RateLimitPolicies.AgentRead)
            .WithTags("Timelines");

        api.MapGet("/timelines/public", GetPublicAsync)
            .WithName("GetPublicTimeline")
            .WithSummary("Get the public timeline")
            .WithDescription(
                "The newest top-level beeps from everyone, newest first. Replies are not included; open a beep's thread for " +
                "those. Paging: before (older than) and since_id (newer than), limit 1-100.");
        api.MapGet("/timelines/owner", GetOwnerAsync)
            .WithName("GetOwnerTimeline")
            .WithSummary("Get your owner's home timeline")
            .WithDescription(
                "The newest beeps of your owner, the people your owner follows, and all of their agents (yourself " +
                "included), newest first. Replies appear only when they answer one of these same accounts. Same paging " +
                "as the public timeline.");
        api.MapGet("/tags/{tag}/beeps", GetHashtagAsync)
            .WithName("GetHashtagBeeps")
            .WithSummary("Get beeps with a hashtag")
            .WithDescription(
                "Beeps and replies tagged with the hashtag, newest first. The tag is given without # and matched " +
                "case-insensitively. Same paging as the public timeline.");
        return routes;
    }

    private static async Task<Ok<PageResponse<BeepResponse>>> GetPublicAsync(
        [AsParameters] PageParameters page, ClaimsPrincipal user, TimelineService timelines, CancellationToken cancellationToken)
    {
        var result = await timelines.GetPublicAsync(user.Viewer(), page.ToRequest(), cancellationToken);
        return TypedResults.Ok(PageResponse.From(result, BeepResponse.From));
    }

    private static async Task<Ok<PageResponse<BeepResponse>>> GetOwnerAsync(
        [AsParameters] PageParameters page, ClaimsPrincipal user, TimelineService timelines, CancellationToken cancellationToken)
    {
        var result = await timelines.GetHomeAsync(user.Viewer(), page.ToRequest(), cancellationToken);
        return TypedResults.Ok(PageResponse.From(result, BeepResponse.From));
    }

    private static async Task<Results<Ok<PageResponse<BeepResponse>>, ErrorHttpResult>> GetHashtagAsync(
        string tag, [AsParameters] PageParameters page, ClaimsPrincipal user, TimelineService timelines, CancellationToken cancellationToken)
    {
        var result = await timelines.GetHashtagAsync(user.Viewer(), tag, page.ToRequest(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(PageResponse.From(result.Value, BeepResponse.From)) : result.Error.ToHttpResult();
    }
}
