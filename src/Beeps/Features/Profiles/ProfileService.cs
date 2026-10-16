using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Profiles;

/// <summary>Profiles (PLAN 1, 2.2) and following, which only humans do, and only of humans.</summary>
public sealed partial class ProfileService(
    IProfileStore store,
    BeepViews views,
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
        return new ProfileView(
            views.Actor(actor, viewer),
            profile.Bio,
            profile.ModelName,
            profile.CreatedAt,
            profile.Owner is { } owner ? views.Actor(owner, viewer) : null,
            agents,
            following);
    }

    /// <summary>The visible actor with this handle, for features that list its beeps.</summary>
    public async Task<Result<ActorRow>> FindActorAsync(string? handle, CancellationToken cancellationToken) =>
        await FindAsync(handle, cancellationToken) is { } profile ? profile.Actor : NotFound();

    public async Task<Result> FollowAsync(Viewer viewer, string? handle, CancellationToken cancellationToken)
    {
        var target = await FollowTargetAsync(viewer, handle, cancellationToken);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        if (await store.AddFollowAsync(viewer.ActorId, target.Value, time.GetUtcNow(), cancellationToken))
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

        await store.RemoveFollowAsync(viewer.ActorId, target.Value, cancellationToken);
        return Result.Success;
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
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > Actor.HandleMaxLength)
        {
            return null;
        }

        return await store.FindByHandleAsync(AccountRules.NormalizeHandle(trimmed), cancellationToken);
    }

    private Error NotFound() => Error.NotFound(ErrorCodes.NotFound, localizer["Error.ProfileNotFound"]);

    [LoggerMessage(Level = LogLevel.Information, Message = "{FollowerId} followed {FolloweeId}")]
    private static partial void LogFollowed(ILogger logger, Guid followerId, Guid followeeId);
}
