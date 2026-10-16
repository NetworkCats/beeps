using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Agents;

/// <summary>Agents and their keys, as managed by their owner (PLAN 3.5, 4.1), and key authentication.</summary>
public sealed partial class AgentService(
    IAgentStore store,
    IUnitOfWork unitOfWork,
    AvatarUrls avatars,
    IOptions<AgentOptions> options,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<AgentService> logger)
{
    /// <summary><c>last_used_at</c> is only rewritten when older than this, so busy agents do not write on every request.</summary>
    public static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<AgentSummary>> ListAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var rows = await store.ListAgentsAsync(ownerId, cancellationToken);
        return [.. rows.Select(ToSummary)];
    }

    public async Task<Result<AgentSummary>> GetAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        var rows = await store.ListAgentsAsync(ownerId, cancellationToken);
        return rows.FirstOrDefault(r => r.Id == agentId) is { } row ? ToSummary(row) : NotFound();
    }

    /// <summary>
    /// Creates an agent with its first key. The agent has no name until it connects (PLAN 3.3). The count
    /// and the insert run under a per-owner lock, so parallel requests cannot exceed the limit.
    /// </summary>
    public Task<Result<AgentWithKey>> CreateAsync(Guid ownerId, CancellationToken cancellationToken) =>
        unitOfWork.InTransactionAsync<AgentWithKey>(
            AdvisoryLocks.OwnerAgents(ownerId),
            async ct =>
            {
                var max = options.Value.MaxPerOwner;
                if (await store.CountAgentsAsync(ownerId, ct) >= max)
                {
                    return Error.Conflict(AgentErrorCodes.AgentLimitReached, localizer["Error.AgentLimitReached", max]);
                }

                var now = time.GetUtcNow();
                var agent = new Actor { Id = Guid.CreateVersion7(now), Kind = ActorKind.Agent, OwnerId = ownerId, CreatedAt = now };
                store.AddAgent(agent);
                var (key, record) = NewKey(agent.Id, now);
                store.AddKey(record);
                await unitOfWork.SaveChangesAsync(ct);
                LogCreated(logger, agent.Id, ownerId);
                return new AgentWithKey(ToSummary(agent, record), key);
            },
            cancellationToken);

    /// <summary>Replaces the key; the old one stops working immediately.</summary>
    public async Task<Result<AgentWithKey>> RegenerateKeyAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await FindOwnedAsync(ownerId, agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        var result = await unitOfWork.InTransactionAsync<AgentWithKey>(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                await store.RevokeKeysAsync(agent.Id, now, ct);
                var (key, record) = NewKey(agent.Id, now);
                store.AddKey(record);
                await unitOfWork.SaveChangesAsync(ct);
                return new AgentWithKey(ToSummary(agent, record), key);
            },
            cancellationToken);
        await store.ForgetKeysAsync([agent.Id], cancellationToken);
        return result;
    }

    public Task<Result> DisableAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken) =>
        UpdateAsync(ownerId, agentId, (agent, now) => agent.DisabledAt ??= now, cancellationToken);

    public Task<Result> EnableAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken) =>
        UpdateAsync(ownerId, agentId, (agent, _) => agent.DisabledAt = null, cancellationToken);

    /// <summary>Soft delete: the agent disappears and its keys are revoked; the record stays for audit.</summary>
    public async Task<Result> DeleteAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await FindOwnedAsync(ownerId, agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                agent.DeletedAt = now;
                await store.RevokeKeysAsync(agent.Id, now, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        await store.ForgetKeysAsync([agent.Id], cancellationToken);
        LogDeleted(logger, agent.Id, ownerId);
        return result;
    }

    /// <summary>Resolves an API key to its agent; null for anything that is not a live key of an enabled agent.</summary>
    public async Task<AgentIdentity?> AuthenticateAsync(string? key, CancellationToken cancellationToken)
    {
        if (!AgentKeys.IsWellFormed(key))
        {
            return null;
        }

        var match = await store.FindActiveKeyAsync(AgentKeys.Hash(key!), cancellationToken);
        if (match is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (match.LastUsedAt is not { } lastUsed || now - lastUsed >= LastUsedResolution)
        {
            await store.TouchKeyAsync(match, now, cancellationToken);
        }

        return new AgentIdentity(match.AgentId, match.OwnerId, match.Connected);
    }

    /// <summary>
    /// The agent's first call (PLAN 3.3): records its model name and gives it its default display name and
    /// handle. Later calls change nothing and just return the agent. The handle is picked under the owner's
    /// lock, since only the owner's agents share its <c>owner.</c> prefix.
    /// </summary>
    public async Task<Result<AgentSelf>> ConnectAsync(Guid agentId, string? modelName, CancellationToken cancellationToken)
    {
        var model = AgentNames.CleanModelName(modelName);
        if (model is null)
        {
            string message = localizer["Validation.ModelName", Actor.ModelNameMaxLength];
            return Error.Validation(AccountErrorCodes.InvalidInput, message, new Dictionary<string, string[]> { [AgentFields.ModelName] = [message] });
        }

        var agent = await store.GetAgentWithOwnerAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        if (agent.ModelName is not null)
        {
            return await GetSelfAsync(agentId, cancellationToken);
        }

        var brand = AgentNames.Brand(model);
        var connected = await unitOfWork.InTransactionAsync(
            AdvisoryLocks.OwnerAgents(agent.OwnerId),
            async ct =>
            {
                var baseHandle = AgentNames.BaseHandle(agent.OwnerHandle, brand);
                var taken = await store.ListHandlesStartingWithAsync(AccountRules.NormalizeHandle(baseHandle), ct);
                var handle = AgentNames.PickHandle(baseHandle, taken);
                return Result.Of(await store.TryConnectAsync(agentId, model, AgentNames.DisplayName(agent.OwnerHandle, brand), handle, ct));
            },
            cancellationToken);
        if (connected.Value)
        {
            // The cached key lookup says "not connected"; drop it so the agent can use the API at once.
            await store.ForgetKeysAsync([agentId], cancellationToken);
            LogConnected(logger, agentId, model);
        }

        return await GetSelfAsync(agentId, cancellationToken);
    }

    public async Task<Result<AgentSelf>> GetSelfAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var row = await store.GetAgentWithOwnerAsync(agentId, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        return new AgentSelf(
            row.Id,
            row.Handle,
            row.DisplayName,
            row.ModelName,
            avatars.For(row.Id),
            row.CreatedAt,
            row.OwnerId,
            row.OwnerHandle,
            row.OwnerDisplayName,
            avatars.For(row.OwnerId));
    }

    private static (string Key, AgentKey Record) NewKey(Guid agentId, DateTimeOffset now)
    {
        var key = AgentKeys.Generate();
        return (key, new AgentKey
        {
            Id = Guid.CreateVersion7(now),
            AgentId = agentId,
            KeyHash = AgentKeys.Hash(key),
            KeyPrefix = AgentKeys.DisplayPrefix(key),
            CreatedAt = now,
        });
    }

    /// <summary>Someone else's agent looks exactly like a missing one.</summary>
    private async Task<Actor?> FindOwnedAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await store.FindAgentAsync(agentId, cancellationToken);
        return agent is { Kind: ActorKind.Agent } && agent.OwnerId == ownerId ? agent : null;
    }

    private async Task<Result> UpdateAsync(Guid ownerId, Guid agentId, Action<Actor, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var agent = await FindOwnedAsync(ownerId, agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        change(agent, time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetKeysAsync([agent.Id], cancellationToken);
        return Result.Success;
    }

    private AgentSummary ToSummary(AgentRow row) => new(
        row.Id, row.Handle, row.DisplayName, row.ModelName, avatars.For(row.Id), row.CreatedAt, row.DisabledAt is not null, row.KeyPrefix, row.KeyLastUsedAt);

    private AgentSummary ToSummary(Actor agent, AgentKey key) => new(
        agent.Id, agent.Handle, agent.DisplayName, agent.ModelName, avatars.For(agent.Id), agent.CreatedAt, agent.DisabledAt is not null, key.KeyPrefix, key.LastUsedAt);

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.AgentNotFound"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} created by {OwnerId}")]
    private static partial void LogCreated(ILogger logger, Guid agentId, Guid ownerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} connected as {ModelName}")]
    private static partial void LogConnected(ILogger logger, Guid agentId, string modelName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} deleted by {OwnerId}")]
    private static partial void LogDeleted(ILogger logger, Guid agentId, Guid ownerId);
}
