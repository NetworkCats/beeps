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
    DateTimeOffset? KeyLastUsedAt,
    string? Bio,
    bool HasCustomAvatar = false)
{
    /// <summary>The agent has called <c>hello</c> and has a name.</summary>
    public bool IsConnected => ModelName is not null;

    /// <summary>The part of the handle after <c>owner.</c>, the only part the owner can change (PLAN 4.2).</summary>
    public string? HandleName => Handle is { } handle && handle.IndexOf('.', StringComparison.Ordinal) is var dot and >= 0 ? handle[(dot + 1)..] : null;
}

/// <param name="HandleName">The part of the handle after <c>owner.</c>.</param>
public sealed record AgentProfileRequest(string? DisplayName, string? HandleName, string? Bio);

/// <summary>The full key: shown exactly once, right after it is generated.</summary>
public sealed record AgentWithKey(AgentSummary Agent, string Key);

/// <summary>Who an API request is acting as.</summary>
public sealed record AgentIdentity(Guid AgentId, Guid OwnerId, bool Connected);

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
    public const string AgentNotConnected = "agent_not_connected";
    public const string HandleTaken = "handle_taken";
}

public static class AgentFields
{
    public const string ModelName = "model_name";

    /// <summary>On the agent page, beside <see cref="Profiles.ProfileFields"/>.</summary>
    public const string HandleName = "HandleName";
}
