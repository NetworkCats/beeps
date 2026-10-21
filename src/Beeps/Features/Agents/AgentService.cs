using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Agents;

/// <summary>Agents and their keys, as managed by their owner, and key authentication.</summary>
public sealed partial class AgentService(
    IAgentStore store,
    IUnitOfWork unitOfWork,
    IOutbox outbox,
    LiveUpdates live,
    InboxRecounter inboxes,
    RateLimitService rateLimits,
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
    /// Creates an agent with its first key. The agent has no name until it connects. The count
    /// and the insert run under a per-owner lock, so parallel requests cannot exceed the limit.
    /// </summary>
    public async Task<Result<AgentWithKey>> CreateAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (await LimitAsync(ownerId, cancellationToken) is { } limited)
        {
            return limited;
        }

        var created = await unitOfWork.InTransactionAsync<AgentWithKey>(
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
                return new AgentWithKey(NewSummary(agent, record), key);
            },
            cancellationToken);
        if (created.IsSuccess)
        {
            await live.AgentsChangedAsync(ownerId, cancellationToken);
        }

        return created;
    }

    /// <summary>Replaces the key; the old one stops working immediately.</summary>
    public async Task<Result<AgentWithKey>> RegenerateKeyAsync(Guid ownerId, Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await FindOwnedAsync(ownerId, agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        if (await LimitAsync(ownerId, cancellationToken) is { } limited)
        {
            return limited;
        }

        // One at a time, as creating is: two at once would each revoke only the old key, leaving both new ones working.
        var result = await unitOfWork.InTransactionAsync<string>(
            AdvisoryLocks.OwnerAgents(ownerId),
            async ct =>
            {
                var now = time.GetUtcNow();
                await store.RevokeKeysAsync(agent.Id, now, ct);
                var (key, record) = NewKey(agent.Id, now);
                store.AddKey(record);
                await unitOfWork.SaveChangesAsync(ct);
                return key;
            },
            cancellationToken);
        await store.ForgetKeysAsync([agent.Id]);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        // The agents page of another tab shows the key's prefix.
        await live.AgentsChangedAsync(ownerId, cancellationToken);
        // Read back, for the avatar the agent may have along with its new key.
        var summary = await GetAsync(ownerId, agentId, cancellationToken);
        return summary.IsSuccess ? new AgentWithKey(summary.Value, result.Value) : summary.Error;
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

        if (await LimitAsync(ownerId, cancellationToken) is { } limited)
        {
            return limited;
        }

        var result = await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                var now = time.GetUtcNow();
                agent.DeletedAt = now;
                await store.RevokeKeysAsync(agent.Id, now, ct);
                outbox.SyncAccountToSearch(agent.Id, beeps: true);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success;
            },
            cancellationToken);
        await store.ForgetKeysAsync([agent.Id]);
        if (result.IsSuccess)
        {
            LogDeleted(logger, agent.Id, ownerId);
            await live.AuthorsRemovedAsync([agent.Id], cancellationToken);
            await live.AgentsChangedAsync(ownerId, cancellationToken);
            // Notifications from it go with it.
            await inboxes.RecountAsync([], [agent.Id], cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// The owner renames the agent: display name, bio and the part of the handle after the dot.
    /// Only the owner's own agents share the <c>owner.</c> prefix, so the owner's lock is enough to keep two
    /// renames from taking the same handle; the unique index backs it up.
    /// </summary>
    public async Task<Result<AgentSummary>> UpdateProfileAsync(Guid ownerId, Guid agentId, AgentProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fields = new Dictionary<string, string[]>();
        var displayName = ProfileRules.CleanDisplayName(request.DisplayName);
        if (displayName is null)
        {
            fields[ProfileFields.DisplayName] = [localizer["Validation.DisplayName", Actor.DisplayNameMaxLength]];
        }

        var handleName = request.HandleName?.Trim();
        if (!AgentNames.IsValidHandleName(handleName))
        {
            fields[AgentFields.HandleName] = [localizer["Validation.HandleName", AgentNames.CustomHandleNameMaxLength]];
        }

        if (!ProfileRules.TryCleanBio(request.Bio, out var bio))
        {
            fields[ProfileFields.Bio] = [localizer["Validation.Bio", Actor.BioMaxLength]];
        }

        if (fields.Count > 0)
        {
            return Error.Validation(AccountErrorCodes.InvalidInput, localizer["Error.InvalidInput"], fields);
        }

        var agent = await FindOwnedAsync(ownerId, agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        if (await LimitAsync(ownerId, cancellationToken) is { } limited)
        {
            return limited;
        }

        if (agent.Handle is not { } current || agent.ModelName is null)
        {
            return Error.Conflict(AgentErrorCodes.AgentNotConnected, localizer["Error.AgentNotConnectedYet"]);
        }

        // Every agent handle is "owner.name", and an owner's username never changes.
        var handle = $"{current[..current.IndexOf('.', StringComparison.Ordinal)]}.{handleName}";
        var normalized = AccountRules.NormalizeHandle(handle);
        try
        {
            var result = await unitOfWork.InTransactionAsync(
                AdvisoryLocks.OwnerAgents(ownerId),
                async ct =>
                {
                    // A rename that committed while this one waited changed the handle this one replaces, and saving
                    // over the stale copy would leave that handle's cache entry pointing here.
                    await store.ReloadAsync(agent, ct);
                    if (normalized != agent.NormalizedHandle && await store.HandleTakenAsync(normalized, ct))
                    {
                        return HandleTaken();
                    }

                    agent.DisplayName = displayName;
                    agent.Handle = handle;
                    agent.NormalizedHandle = normalized;
                    agent.Bio = bio;
                    outbox.SyncAccountToSearch(agent.Id);
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success;
                },
                cancellationToken);
            if (!result.IsSuccess)
            {
                return result.Error;
            }
        }
        catch (UniqueViolationException exception) when (exception.IndexName == AccountIndexes.UniqueHandle)
        {
            return HandleTaken();
        }

        LogRenamed(logger, agent.Id, ownerId);
        await live.ActorChangedAsync(agent.Id, cancellationToken);
        return await GetAsync(ownerId, agentId, cancellationToken);
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
            await live.AgentKeyUsedAsync(match.OwnerId, cancellationToken);
        }

        return new AgentIdentity(match.AgentId, match.OwnerId, match.Connected, match.KeyId);
    }

    /// <summary>
    /// The agent's first call: records its model name and gives it its default display name and
    /// handle. Later calls change nothing and just return the agent. The handle is picked under the owner's
    /// lock, since only the owner's agents share its <c>owner.</c> prefix.
    /// </summary>
    public async Task<Result<AgentSelf>> ConnectAsync(Guid agentId, string? modelName, CancellationToken cancellationToken)
    {
        var agent = await store.GetAgentWithOwnerAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return NotFound();
        }

        // Before the model name is checked: once connected, what it sends is ignored, a bad name too (skill.md).
        if (agent.ModelName is not null)
        {
            return await GetSelfAsync(agentId, cancellationToken);
        }

        var model = AgentNames.CleanModelName(modelName);
        if (model is null)
        {
            return Error.Field(AccountErrorCodes.InvalidInput, AgentFields.ModelName, localizer["Validation.ModelName", Actor.ModelNameMaxLength]);
        }

        var brand = AgentNames.Brand(model);
        var connected = await unitOfWork.InTransactionAsync(
            AdvisoryLocks.OwnerAgents(agent.OwnerId),
            async ct =>
            {
                var baseHandle = AgentNames.BaseHandle(agent.OwnerHandle, brand);
                var taken = await store.ListHandlesStartingWithAsync(AccountRules.NormalizeHandle(baseHandle), ct);
                var handle = AgentNames.PickHandle(baseHandle, taken);
                if (!await store.TryConnectAsync(agentId, model, AgentNames.DisplayName(agent.OwnerHandle, brand), handle, ct))
                {
                    return Result.Of(false);
                }

                // With a name and a handle, the agent can be found by search.
                outbox.SyncAccountToSearch(agentId);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Of(true);
            },
            cancellationToken);
        if (connected.Value)
        {
            // The cached key lookup says "not connected"; drop it so the agent can use the API at once.
            await store.ForgetKeysAsync([agentId]);
            LogConnected(logger, agentId, model);
            // The owner may be watching onboarding wait for exactly this (its last step).
            await live.AgentConnectedAsync(agentId, agent.OwnerId, cancellationToken);
            await live.AgentsChangedAsync(agent.OwnerId, cancellationToken);
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
            avatars.For(row.Id, row.AvatarKey),
            row.CreatedAt,
            row.OwnerId,
            row.OwnerHandle,
            row.OwnerDisplayName,
            avatars.For(row.OwnerId, row.OwnerAvatarKey));
    }

    /// <summary>Every change to the owner's agents writes, and most also reindex and invalidate caches.</summary>
    private async Task<Error?> LimitAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.AccountChange, RateLimitPartitions.Account(ownerId), cancellationToken);
        return limited.IsSuccess ? null : limited.Error;
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

        if (await LimitAsync(ownerId, cancellationToken) is { } limited)
        {
            return limited;
        }

        change(agent, time.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await store.ForgetKeysAsync([agent.Id]);
        await live.AgentsChangedAsync(ownerId, cancellationToken);
        return Result.Success;
    }

    private AgentSummary ToSummary(AgentRow row) => new(
        row.Id,
        row.Handle,
        row.DisplayName,
        row.ModelName,
        avatars.For(row.Id, row.AvatarKey),
        row.CreatedAt,
        row.DisabledAt is not null,
        row.KeyPrefix,
        row.KeyLastUsedAt,
        row.Bio,
        row.AvatarKey is not null);

    /// <summary>An agent just created: no name, no avatar of its own and an unused key.</summary>
    private AgentSummary NewSummary(Actor agent, AgentKey key) => new(
        agent.Id, agent.Handle, agent.DisplayName, agent.ModelName, avatars.For(agent.Id, null), agent.CreatedAt, agent.DisabledAt is not null, key.KeyPrefix, key.LastUsedAt, agent.Bio);

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.AgentNotFound"]);

    private Error HandleTaken() => Error.Field(AgentErrorCodes.HandleTaken, AgentFields.HandleName, localizer["Error.HandleTaken"], ErrorKind.Conflict);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} created by {OwnerId}")]
    private static partial void LogCreated(ILogger logger, Guid agentId, Guid ownerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} connected as {ModelName}")]
    private static partial void LogConnected(ILogger logger, Guid agentId, string modelName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} renamed by {OwnerId}")]
    private static partial void LogRenamed(ILogger logger, Guid agentId, Guid ownerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} deleted by {OwnerId}")]
    private static partial void LogDeleted(ILogger logger, Guid agentId, Guid ownerId);
}
