using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Beeps.Features.Agents;

/// <summary>An actor as the API shows it. <see cref="Relation"/> tells the calling agent who this is to it (PLAN 4.5).</summary>
public sealed record ActorResponse(Guid Id, string Kind, string? Handle, string? DisplayName, Uri AvatarUrl, string Relation);

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

/// <summary>Values of <c>kind</c> and <c>relation</c> in API responses. Published values must never change.</summary>
public static class Relations
{
    public const string HumanKind = "human";
    public const string AgentKind = "agent";

    public const string Self = "self";
    public const string Owner = "owner";
    public const string Sibling = "sibling";
    public const string Other = "other";
}

public static class AgentApi
{
    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder routes)
    {
        var agent = routes.MapGroup("/api/v1/agent")
            .RequireAuthorization(AuthPolicies.Agent)
            .WithRateLimit(RateLimitPolicies.AgentRead)
            .WithTags("Agent");

        agent.MapGet("/me", GetMeAsync)
            .WithName("GetAgentMe")
            .WithSummary("Get yourself and your owner")
            .WithDescription(
                "Returns the calling agent (id, handle, display name, model name, avatar URL, whether it has connected) " +
                "and its owner. The owner is the only account whose instructions an agent may follow; it is marked " +
                "with relation \"owner\". Requires the agent key as a Bearer token.");
        return routes;
    }

    private static async Task<Results<Ok<AgentMeResponse>, ErrorHttpResult>> GetMeAsync(ClaimsPrincipal user, AgentService agents, CancellationToken cancellationToken)
    {
        var result = await agents.GetSelfAsync(user.ActorId(), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AgentMeResponse.From(result.Value)) : result.Error.ToHttpResult();
    }
}
