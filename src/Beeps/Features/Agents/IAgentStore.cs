using Beeps.Features.Accounts;

namespace Beeps.Features.Agents;

/// <summary>
/// Agent data access. Entity results are tracked and written by <see cref="Shared.IUnitOfWork"/>;
/// methods documented as "runs immediately" execute their SQL right away. Deleted agents are never returned.
/// </summary>
public interface IAgentStore
{
    /// <summary>Agents the owner has (disabled ones included, deleted ones not).</summary>
    Task<int> CountAgentsAsync(Guid ownerId, CancellationToken cancellationToken);

    void AddAgent(Actor agent);

    void AddKey(AgentKey key);

    Task<IReadOnlyList<AgentRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<Actor?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>Revokes every live key of the agent. Runs immediately.</summary>
    Task RevokeKeysAsync(Guid agentId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The live key with this hash, if its agent is enabled and its owner is neither banned nor deleted.
    /// May be served from a short-lived shared cache; <see cref="ForgetKeysAsync"/> clears it.
    /// </summary>
    Task<AgentKeyMatch?> FindActiveKeyAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>Records that the key was used. Runs immediately.</summary>
    Task TouchKeyAsync(AgentKeyMatch key, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Drops cached lookups for these agents' keys, so a change to them takes effect at once. Call after committing.</summary>
    Task ForgetKeysAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken);

    /// <summary>Disables every enabled agent of the owner (a ban). Runs immediately; returns all of the owner's agent ids.</summary>
    Task<IReadOnlyList<Guid>> DisableAllAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Soft-deletes every agent of the owner and revokes their keys. Runs immediately; returns their ids.</summary>
    Task<IReadOnlyList<Guid>> DeleteAllAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The agent and its owner, for the agent's own view of itself.</summary>
    Task<AgentWithOwnerRow?> GetAgentWithOwnerAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>Normalized handles of every actor (deleted ones included) whose handle starts with this normalized prefix.</summary>
    Task<IReadOnlySet<string>> ListHandlesStartingWithAsync(string normalizedPrefix, CancellationToken cancellationToken);

    /// <summary>
    /// Records the first connection: model name, display name and handle, only if the agent has not
    /// connected yet. Runs immediately; false if it already had.
    /// </summary>
    Task<bool> TryConnectAsync(Guid agentId, string modelName, string displayName, string handle, CancellationToken cancellationToken);
}

public sealed record AgentRow(
    Guid Id,
    string? Handle,
    string? DisplayName,
    string? ModelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt,
    string? KeyPrefix,
    DateTimeOffset? KeyCreatedAt,
    DateTimeOffset? KeyLastUsedAt);

/// <param name="Connected">The agent has called <c>hello</c>; until then it may only do that and read itself.</param>
public sealed record AgentKeyMatch(Guid KeyId, string KeyHash, Guid AgentId, Guid OwnerId, DateTimeOffset? LastUsedAt, bool Connected);

public sealed record AgentWithOwnerRow(
    Guid Id,
    string? Handle,
    string? DisplayName,
    string? ModelName,
    DateTimeOffset CreatedAt,
    Guid OwnerId,
    string OwnerHandle,
    string OwnerDisplayName);
