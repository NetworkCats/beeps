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

    /// <summary>Optional, so it must be nullable: a minimal API treats a plain bool in the query as required.</summary>
    [FromQuery(Name = "all")]
    public bool? All { get; init; }
}

public static class LiveEndpoints
{
    /// <summary>The browsers' stream, under the cookie scheme like every page (PLAN 6.14).</summary>
    public const string WebPath = "/stream";

    public const string AgentPath = "/api/v1/stream";

    /// <summary>How the request is signed in, as pages write it in <c>data-auth</c>; a page whose stream keeps failing asks.</summary>
    public const string AuthPath = "/stream/auth";

    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(WebPath, OpenWebAsync)
            // Signed in, verified or not: an unverified tab follows its account (verified in another tab).
            .RequireAuthorization(AuthPolicies.SignedIn)
            .WithRateLimit(RateLimitPolicies.StreamConnect)
            .ExcludeFromDescription();

        // Open to everyone: a tab whose session ended while its stream was down finds out here, and shows itself signed out.
        routes.MapGet(AuthPath, (ClaimsPrincipal user, HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Text(user.AuthState());
            })
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
                "A \"close\" event means this stream was ended for good: you opened too many, or your key stopped working " +
                "(regenerated or revoked, or you were disabled or deleted). Do not reconnect it. Limited to " +
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
        var result = await live.OpenAsync(
            user.Viewer(),
            new LiveSession(user.SessionId(), user.IsMember()),
            new LiveRequest(query.Watch, query.Beeps, lastEventId ?? query.LastEventId, query.From, query.All == true),
            cancellationToken);
        return result.IsSuccess ? Stream(context, result.Value, cancellationToken) : result.Error.ToHttpResult();
    }

    private static Results<ServerSentEventsResult<string>, ErrorHttpResult> OpenAgent(
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId, ClaimsPrincipal user, HttpContext context, LiveService live, CancellationToken cancellationToken)
    {
        var result = live.OpenInbox(user.Viewer(), user.AgentKeyId(), lastEventId, cancellationToken);
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
