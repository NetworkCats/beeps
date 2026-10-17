using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Profiles;

/// <summary>Profiles (PLAN 1, 2.2), following, which only humans do and only of humans, and blocking (PLAN 5.1).</summary>
public sealed partial class ProfileService(
    IProfileStore store,
    BeepViews views,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IStringLocalizer<SharedResource> localizer,
    ILogger<ProfileService> logger)
{
    /// <summary>Accepts a handle with or without its leading <c>@</c>.</summary>
    public async Task<Result<ProfileView>> GetAsync(Viewer? viewer, string? handle, CancellationToken cancellationToken)
    {
        var profile = await FindAsync(handle, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        var actor = profile.Actor;
        IReadOnlyList<ActorView> agents = actor.Kind == ActorKind.Human
            ? [.. (await store.ListAgentsAsync(actor.Id, cancellationToken)).Select(a => views.Actor(a, viewer))]
            : [];
        bool? following = viewer is { IsAgent: false } && actor.Kind == ActorKind.Human && actor.Id != viewer.ActorId
            ? await store.IsFollowingAsync(viewer.ActorId, actor.Id, cancellationToken)
            : null;
        bool? blocked = viewer is { IsAgent: false } && !IsOwn(viewer, actor)
            ? await store.IsBlockingAsync(viewer.ActorId, actor.Id, cancellationToken)
            : null;
        return new ProfileView(
            views.Actor(actor, viewer),
            profile.Bio,
            profile.ModelName,
            profile.CreatedAt,
            profile.Owner is { } owner ? views.Actor(owner, viewer) : null,
            agents,
            following,
            blocked);
    }

    /// <summary>The visible actor with this handle, for features that list its beeps.</summary>
    public async Task<Result<ActorRow>> FindActorAsync(string? handle, CancellationToken cancellationToken) =>
        await FindAsync(handle, cancellationToken) is { } profile ? profile.Actor : NotFound();

    /// <summary>Not while either of the two has blocked the other.</summary>
    public async Task<Result> FollowAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        var target = await FollowTargetAsync(viewer, handle, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        if (await store.AnyBlockBetweenAsync(viewer.ActorId, target.Value, cancellationToken))
        {
            return Error.Forbidden(ProfileErrorCodes.Blocked, localizer["Error.Blocked"]);
        }

        // In a transaction, so the cached list of whom the viewer follows is invalidated with it (PLAN 6.7).
        var followed = await unitOfWork.InTransactionAsync<bool>(
            null,
            async ct => Result.Of(await store.AddFollowAsync(viewer.ActorId, target.Value, time.GetUtcNow(), ct)),
            cancellationToken);
        if (followed.Value)
        {
            LogFollowed(logger, viewer.ActorId, target.Value);
        }

        return Result.Success;
    }

    public async Task<Result> UnfollowAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        var target = await FollowTargetAsync(viewer, handle, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        return await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                await store.RemoveFollowAsync(viewer.ActorId, target.Value, ct);
                return Result.Success;
            },
            cancellationToken);
    }

    /// <summary>
    /// Blocks a human or an agent. Blocking a human also ends following in both directions: neither would see
    /// the other's beeps anyway.
    /// </summary>
    public async Task<Result> BlockAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        var target = await BlockTargetAsync(viewer, handle, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var actor = target.Value;
        var added = false;
        await unitOfWork.InTransactionAsync(
            null,
            async ct =>
            {
                added = await store.AddBlockAsync(viewer.ActorId, actor.Id, time.GetUtcNow(), ct);
                if (actor.Kind == ActorKind.Human)
                {
                    await store.RemoveFollowAsync(viewer.ActorId, actor.Id, ct);
                    await store.RemoveFollowAsync(actor.Id, viewer.ActorId, ct);
                }

                return Result.Success;
            },
            cancellationToken);
        if (added)
        {
            LogBlocked(logger, viewer.ActorId, actor.Id);
        }

        return Result.Success;
    }

    public async Task<Result> UnblockAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        var target = await BlockTargetAsync(viewer, handle, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        await store.RemoveBlockAsync(viewer.ActorId, target.Value.Id, cancellationToken);
        return Result.Success;
    }

    private async Task<Result<ActorRow>> BlockTargetAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.IsAgent)
        {
            return Error.Forbidden(ProfileErrorCodes.AgentsCannotBlock, localizer["Error.AgentsCannotBlock"]);
        }

        var profile = await FindAsync(handle, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        return IsOwn(viewer, profile.Actor)
            ? Error.Validation(ProfileErrorCodes.CannotBlockOwn, localizer["Error.CannotBlockOwn"])
            : profile.Actor;
    }

    /// <summary>Oneself, or one of one's agents: never blocked.</summary>
    private static bool IsOwn(Viewer viewer, ActorRow actor) => actor.Id == viewer.ActorId || actor.OwnerId == viewer.ActorId;

    private async Task<Result<Guid>> FollowTargetAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.IsAgent)
        {
            return Error.Forbidden(ProfileErrorCodes.AgentsCannotFollow, localizer["Error.AgentsCannotFollow"]);
        }

        var profile = await FindAsync(handle, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.Actor.Kind != ActorKind.Human)
        {
            return Error.Validation(ProfileErrorCodes.CanOnlyFollowHumans, localizer["Error.CanOnlyFollowHumans"]);
        }

        return profile.Actor.Id == viewer.ActorId
            ? Error.Validation(ProfileErrorCodes.CannotFollowSelf, localizer["Error.CannotFollowSelf"])
            : profile.Actor.Id;
    }

    private async Task<ProfileRow?> FindAsync(string? handle, CancellationToken cancellationToken)
    {
        var trimmed = handle?.Trim().TrimStart('@');
        if (!AccountRules.IsPlausibleHandle(trimmed))
        {
            return null;
        }

        return await store.FindByHandleAsync(AccountRules.NormalizeHandle(trimmed!), cancellationToken);
    }

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.ProfileNotFound"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "{FollowerId} followed {FolloweeId}")]
    private static partial void LogFollowed(ILogger logger, Guid followerId, Guid followeeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{BlockerId} blocked {BlockedId}")]
    private static partial void LogBlocked(ILogger logger, Guid blockerId, Guid blockedId);
}
