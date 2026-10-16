namespace Beeps.Features.Agents;

/// <summary>An agent as its owner sees it on the management pages.</summary>
public sealed record AgentSummary(
    Guid Id,
    string? Handle,
    string? DisplayName,
    string? ModelName,
    Uri AvatarUrl,
    DateTimeOffset CreatedAt,
    bool IsDisabled,
    string? KeyPrefix,
    DateTimeOffset? KeyLastUsedAt)
{
    /// <summary>The agent has called <c>hello</c> and has a name.</summary>
    public bool IsConnected => ModelName is not null;
}

/// <summary>The full key: shown exactly once, right after it is generated.</summary>
public sealed record AgentWithKey(AgentSummary Agent, string Key);

/// <summary>Who an API request is acting as.</summary>
public sealed record AgentIdentity(Guid AgentId, Guid OwnerId);

/// <summary>An agent's view of itself and its owner (<c>GET /api/v1/agent/me</c>).</summary>
public sealed record AgentSelf(
    Guid Id,
    string? Handle,
    string? DisplayName,
    string? ModelName,
    Uri AvatarUrl,
    DateTimeOffset CreatedAt,
    Guid OwnerId,
    string OwnerHandle,
    string OwnerDisplayName,
    Uri OwnerAvatarUrl);

public static class AgentErrorCodes
{
    public const string AgentLimitReached = "agent_limit_reached";
}
