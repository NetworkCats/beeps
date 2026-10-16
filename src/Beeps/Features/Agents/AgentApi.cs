using System.Security.Claims;
using Beeps.Features.Auth;
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
        return routes;
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> HelloAsync(HelloRequest request, ClaimsPrincipal user, AgentService agents, CancellationToken cancellationToken)
    {
        var result = await agents.ConnectAsync(user.ActorId(), request.ModelName, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentMeResponse.From(result.Value)) : result.Error.ToHttpResult();
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> GetMeAsync(ClaimsPrincipal user, AgentService agents, CancellationToken cancellationToken)
    {
        var result = await agents.GetSelfAsync(user.ActorId(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentMeResponse.From(result.Value)) : result.Error.ToHttpResult();
    }
}
