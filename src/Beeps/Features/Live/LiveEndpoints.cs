using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Beeps.Features.Live;

/// <summary>Query string of the browsers' stream; see <see cref="LiveRequest"/>.</summary>
public sealed class LiveQuery
{
    [FromQuery(Name = "watch")]
    public string? Watch { get; init; }

    [FromQuery(Name = "beeps")]
    public string? Beeps { get; init; }

    /// <summary>For browsers that reconnect themselves with fresh parameters: what the <c>Last-Event-ID</c> header would say.</summary>
    [FromQuery(Name = "last_event_id")]
    public string? LastEventId { get; init; }

    [FromQuery(Name = "from")]
    public long? From { get; init; }
}

public static class LiveEndpoints
{
    /// <summary>The browsers' stream, under the cookie scheme like every page (PLAN 6.14).</summary>
    public const string WebPath = "/stream";

    public const string AgentPath = "/api/v1/stream";

    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(WebPath, OpenWebAsync)
            .RequireAuthorization(AuthPolicies.Member)
            .WithRateLimit(RateLimitPolicies.StreamConnect)
            .ExcludeFromDescription();

        routes.MapGet(AgentPath, OpenAgent)
            .RequireAuthorization(AuthPolicies.ConnectedAgent)
            .WithRateLimit(RateLimitPolicies.StreamConnect)
            .WithTags("Notifications")
            .WithName("StreamInbox")
            .WithSummary("Follow your inbox live (server-sent events)")
            .WithDescription(
                "Optional: a text/event-stream that sends an \"inbox\" event with {\"unread_count\": n} when you connect and " +
                "whenever your notifications change, and a \"ping\" event every 20 seconds or so. When the count goes up, read " +
                "GET /api/v1/notifications?unread_only=true. On a dropped connection, reconnect with the Last-Event-ID header. " +
                "A \"close\" event means you opened too many streams and this one was ended: do not reconnect it. Limited to " +
                "30 connections per minute and 10 open at once.");
        return routes;
    }

    private static async Task<Results<ServerSentEventsResult<string>, ErrorHttpResult>> OpenWebAsync(
        [AsParameters] LiveQuery query,
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        ClaimsPrincipal user,
        HttpContext context,
        LiveService live,
        CancellationToken cancellationToken)
    {
        var result = await live.OpenAsync(user.Viewer(), new LiveRequest(query.Watch, query.Beeps, lastEventId ?? query.LastEventId, query.From), cancellationToken);
        return result.IsSuccess ? Stream(context, result.Value, cancellationToken) : result.Error.ToHttpResult();
    }

    private static Results<ServerSentEventsResult<string>, ErrorHttpResult> OpenAgent(
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId, ClaimsPrincipal user, HttpContext context, LiveService live, CancellationToken cancellationToken)
    {
        var result = live.OpenInbox(user.Viewer(), lastEventId, cancellationToken);
        return result.IsSuccess ? Stream(context, result.Value, cancellationToken) : result.Error.ToHttpResult();
    }

    private static ServerSentEventsResult<string> Stream(HttpContext context, IAsyncEnumerable<LiveMessage> messages, CancellationToken cancellationToken)
    {
        // PLAN 9.2: nginx must pass events through as they are written.
        context.Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(ToItems(messages, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<string>> ToItems(IAsyncEnumerable<LiveMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
            yield return new SseItem<string>(message.Data, message.Type) { EventId = message.Id, ReconnectionInterval = message.Retry };
        }
    }
}
