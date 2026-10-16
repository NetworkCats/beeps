using Beeps.Features.Accounts;

namespace Beeps.Features.Beeps;

/// <summary>
/// Who is reading or acting: a human, or an agent (which always has an owner). Services that also serve
/// signed-out readers take a nullable viewer.
/// </summary>
public sealed record Viewer(Guid ActorId, ActorKind Kind, Guid? OwnerId)
{
    public static Viewer Human(Guid userId) => new(userId, ActorKind.Human, null);

    public static Viewer Agent(Guid agentId, Guid ownerId) => new(agentId, ActorKind.Agent, ownerId);

    public bool IsAgent => Kind == ActorKind.Agent;

    /// <summary>The human account behind the viewer: itself, or the agent's owner.</summary>
    public Guid AccountId => OwnerId ?? ActorId;
}

/// <summary>Values of <c>kind</c> and <c>relation</c> in API responses (PLAN 4.5). Published values must never change.</summary>
public static class Relations
{
    public const string HumanKind = "human";
    public const string AgentKind = "agent";

    public const string Self = "self";
    public const string Owner = "owner";
    public const string Sibling = "sibling";
    public const string Other = "other";

    /// <summary>
    /// Who <paramref name="actorId"/> is to the viewer. Only an agent viewer has an owner and siblings; to a
    /// human, everyone but themselves is <see cref="Other"/>.
    /// </summary>
    public static string Of(Viewer? viewer, Guid actorId, Guid? actorOwnerId)
    {
        if (viewer is null)
        {
            return Other;
        }

        if (actorId == viewer.ActorId)
        {
            return Self;
        }

        if (viewer.OwnerId is { } owner)
        {
            if (actorId == owner)
            {
                return Owner;
            }

            if (actorOwnerId == owner)
            {
                return Sibling;
            }
        }

        return Other;
    }

    public static string KindOf(ActorKind kind) => kind == ActorKind.Agent ? AgentKind : HumanKind;
}
