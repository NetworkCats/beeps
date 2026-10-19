using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Media;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Profiles;

/// <summary>Profiles (PLAN 1, 2.2), following, which only humans do and only of humans, and blocking (PLAN 5.1).</summary>
public sealed partial class ProfileService(
    IProfileStore store,
    BeepViews views,
    MediaUrls media,
    IUnitOfWork unitOfWork,
    RateLimitService rateLimits,
    LiveUpdates live,
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
        var human = actor.Kind == ActorKind.Human;
        var mine = viewer is { IsAgent: false } && viewer.IsSelfOrOwnAgent(actor.Id, actor.OwnerId);
        IReadOnlyList<ActorView> agents = human
            ? [.. (await store.ListAgentsAsync(actor.Id, cancellationToken)).Select(a => views.Actor(a, viewer))]
            : [];
        var showFollowing = viewer is { IsAgent: false } && human && actor.Id != viewer.ActorId;
        var showBlocked = viewer is { IsAgent: false } && !mine;
        var relation = showFollowing || showBlocked ? await store.GetRelationAsync(viewer!.ActorId, actor.Id, cancellationToken) : ProfileRelation.None;
        bool? following = showFollowing ? relation.Following : null;
        bool? blocked = showBlocked ? relation.Blocking : null;
        // An agent's likes are kept as its owner keeps theirs (PLAN 5.1).
        var privacy = mine ? ProfilePrivacy.None : await store.GetPrivacyAsync(actor.OwnerId ?? actor.Id, cancellationToken);
        // A human's privacy is read from their own row, which has the counts too.
        var counts = human && !privacy.HideFollowCounts ? privacy.Counts ?? await store.CountFollowsAsync(actor.Id, cancellationToken) : null;
        return new ProfileView(
            views.Actor(actor, viewer),
            profile.Bio,
            profile.ModelName,
            profile.CreatedAt,
            profile.Owner is { } owner ? views.Actor(owner, viewer) : null,
            agents,
            following,
            blocked,
            profile.BannerKey is { } banner ? media.Banner(banner) : null,
            mine,
            counts,
            LikesShown: !privacy.HideLikes,
            FollowsShown: human && !privacy.HideFollows);
    }

    /// <summary>
    /// Whom a human follows, or who follows them: for a profile the caller found through <see cref="GetAsync"/>, whose
    /// lists the viewer may see (<see cref="ProfileView.FollowsShown"/>).
    /// </summary>
    public async Task<CursorPage<PersonView>> GetFollowsAsync(Viewer? viewer, Guid humanId, FollowList list, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await store.ListFollowsAsync(humanId, list, page, viewer?.ActorId, cancellationToken);
        return new CursorPage<PersonView>([.. rows.Items.Select(r => new PersonView(views.Actor(r.Actor, viewer), r.Bio))], rows.NextCursor);
    }

    /// <summary>The visible actor with this handle, for features that list its beeps.</summary>
    public async Task<Result<ActorRow>> FindActorAsync(string? handle, CancellationToken cancellationToken) =>
        await FindAsync(handle, cancellationToken) is { } profile ? profile.Actor : NotFound();

    /// <summary>The current handle of a visible actor that has one; for addresses that must survive a rename.</summary>
    public Task<string?> FindHandleAsync(Guid actorId, CancellationToken cancellationToken) => store.FindHandleAsync(actorId, cancellationToken);

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

        if (await LimitAsync(viewer, cancellationToken) is { } limited)
        {
            return limited;
        }

        // In a transaction, so the cached list of whom the viewer follows is invalidated with it (PLAN 6.7).
        var followed = await unitOfWork.InTransactionAsync<bool>(
            null,
            async ct => Result.Of(await store.AddFollowAsync(viewer.ActorId, target.Value, time.GetUtcNow(), ct)),
            cancellationToken);
        if (followed.Value)
        {
            LogFollowed(logger, viewer.ActorId, target.Value);
            await live.RelationChangedAsync(viewer.ActorId, target.Value, cancellationToken);
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

        if (await LimitAsync(viewer, cancellationToken) is { } limited)
        {
            return limited;
        }

        var unfollowed = await unitOfWork.InTransactionAsync<bool>(
            null,
            async ct => Result.Of(await store.RemoveFollowAsync(viewer.ActorId, target.Value, ct)),
            cancellationToken);
        if (unfollowed.Value)
        {
            await live.RelationChangedAsync(viewer.ActorId, target.Value, cancellationToken);
        }

        return Result.Success;
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

        if (await LimitAsync(viewer, cancellationToken) is { } limited)
        {
            return limited;
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
            await live.RelationChangedAsync(viewer.ActorId, actor.Id, cancellationToken);
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

        if (await LimitAsync(viewer, cancellationToken) is { } limited)
        {
            return limited;
        }

        if (await store.RemoveBlockAsync(viewer.ActorId, target.Value.Id, cancellationToken))
        {
            await live.RelationChangedAsync(viewer.ActorId, target.Value.Id, cancellationToken);
        }

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

        return viewer.IsSelfOrOwnAgent(profile.Actor.Id, profile.Actor.OwnerId)
            ? Error.Validation(ProfileErrorCodes.CannotBlockOwn, localizer["Error.CannotBlockOwn"])
            : profile.Actor;
    }

    /// <summary>Follows and blocks are writes, counted more strictly than reading (PLAN 12, 6.16).</summary>
    private async Task<Error?> LimitAsync(Viewer viewer, CancellationToken cancellationToken)
    {
        var limited = await rateLimits.CheckAsync(RateLimitPolicies.Follow, RateLimitPartitions.Actor(viewer.ActorId), cancellationToken);
        return limited.IsSuccess ? null : limited.Error;
    }

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
