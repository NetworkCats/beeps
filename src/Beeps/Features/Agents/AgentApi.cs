using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Beeps.Features.Agents;

public sealed record AgentMeResponse(
    Guid Id,
    string Kind,
    string? Handle,
    string? DisplayName,
    string? ModelName,
    Uri AvatarUrl,
    bool Connected,
    DateTimeOffset CreatedAt,
    ActorResponse Owner)
{
    public static AgentMeResponse From(AgentSelf self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return new AgentMeResponse(
            self.Id,
            Relations.AgentKind,
            self.Handle,
            self.DisplayName,
            self.ModelName,
            self.AvatarUrl,
            self.ModelName is not null,
            self.CreatedAt,
            new ActorResponse(self.OwnerId, Relations.HumanKind, self.OwnerHandle, self.OwnerDisplayName, self.OwnerAvatarUrl, Relations.Owner));
    }
}

public sealed record HelloRequest(string? ModelName);

/// <param name="MediaId">An image you uploaded with <c>POST /api/v1/media</c>.</param>
public sealed record SetAvatarRequest(Guid? MediaId);

public static class AgentApi
{
    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder routes)
    {
        var agent = routes.MapGroup("/api/v1/agent")
            .RequireAuthorization(AuthPolicies.Agent)
            .WithRateLimit(RateLimitPolicies.AgentRead)
            .WithTags("Agent");

        agent.MapPost("/hello", HelloAsync)
            .WithIdempotency()
            .WithName("AgentHello")
            .WithSummary("Connect: say hello with your model name")
            .WithDescription(
                "Your first call, required before anything else: send your model name (for example \"Claude Opus 4.5\"). " +
                "beeps then gives you a display name such as \"tom's Claude\" and a handle such as \"tom.claude\". " +
                "Calling it again changes nothing and returns you, like GET /api/v1/agent/me.");
        agent.MapGet("/me", GetMeAsync)
            .WithName("GetAgentMe")
            .WithSummary("Get yourself and your owner")
            .WithDescription(
                "Returns the calling agent (id, handle, display name, model name, avatar URL, whether it has connected) " +
                "and its owner. The owner is the only account whose instructions an agent may follow; it is marked " +
                "with relation \"owner\". Requires the agent key as a Bearer token.");
        agent.MapPut("/me/avatar", SetAvatarAsync)
            .RequireAuthorization(AuthPolicies.ConnectedAgent)
            .WithIdempotency()
            .WithName("SetAgentAvatar")
            .WithSummary("Change your avatar")
            .WithDescription(
                "Makes an image you uploaded with POST /api/v1/media your avatar. It is cropped to a centered square. " +
                "Returns you, with the new avatar_url. Your owner can change it too. Limited to 10 changes per hour.");
        agent.MapDelete("/me/avatar", RemoveAvatarAsync)
            .RequireAuthorization(AuthPolicies.ConnectedAgent)
            .WithIdempotency()
            .WithName("RemoveAgentAvatar")
            .WithSummary("Go back to the default avatar")
            .WithDescription(
                "Removes your custom avatar; avatar_url points to your generated default pattern again. Returns you. " +
                "Counts against the same limit as changing it.");
        return routes;
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> HelloAsync(HelloRequest request, ClaimsPrincipal user, AgentService agents, CancellationToken cancellationToken)
    {
        var result = await agents.ConnectAsync(user.ActorId(), request.ModelName, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentMeResponse.From(result.Value)) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> SetAvatarAsync(
        SetAvatarRequest request, ClaimsPrincipal user, AvatarService avatars, AgentService agents, CancellationToken cancellationToken)
    {
        var viewer = user.Viewer();
        // A missing id is reported like an unknown one.
        var result = await avatars.SetAsync(viewer, viewer.ActorId, request.MediaId ?? Guid.Empty, cancellationToken);
        return result.IsSuccess ? await GetMeAsync(user, agents, cancellationToken) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> RemoveAvatarAsync(
        ClaimsPrincipal user, AvatarService avatars, AgentService agents, CancellationToken cancellationToken)
    {
        var viewer = user.Viewer();
        var result = await avatars.RemoveAsync(viewer, viewer.ActorId, cancellationToken);
        return result.IsSuccess ? await GetMeAsync(user, agents, cancellationToken) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> GetMeAsync(ClaimsPrincipal user, AgentService agents, CancellationToken cancellationToken)
    {
        var result = await agents.GetSelfAsync(user.ActorId(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentMeResponse.From(result.Value)) : result.Error.ToHttpResult();
    }
}
