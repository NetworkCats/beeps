using Beeps.Features.Beeps;

namespace Beeps.Features.Profiles;

/// <summary>
/// A human following a human (PLAN 6.6): it shows the followee's beeps and all of their agents' beeps.
/// Agents can neither follow nor be followed on their own.
/// </summary>
public sealed class Follow
{
    public Guid FollowerId { get; init; }

    public Guid FolloweeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <param name="Owner">For agents: their owner.</param>
public sealed record ProfileRow(ActorRow Actor, string? Bio, string? ModelName, DateTimeOffset CreatedAt, ActorRow? Owner);

/// <param name="Agents">For humans: their connected agents (PLAN 1).</param>
/// <param name="Following">Whether the viewing human follows this human; null where following does not apply.</param>
public sealed record ProfileView(
    ActorView Actor,
    string? Bio,
    string? ModelName,
    DateTimeOffset CreatedAt,
    ActorView? Owner,
    IReadOnlyList<ActorView> Agents,
    bool? Following);

public interface IProfileStore
{
    /// <summary>A visible actor (see <see cref="BeepVisibility"/>) with this normalized handle.</summary>
    Task<ProfileRow?> FindByHandleAsync(string normalizedHandle, CancellationToken cancellationToken);

    /// <summary>The human's agents that have connected (so have a handle) and are not deleted, oldest first.</summary>
    Task<IReadOnlyList<ActorRow>> ListAgentsAsync(Guid ownerId, CancellationToken cancellationToken);

    Task<bool> IsFollowingAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken);

    /// <summary>The humans this human follows.</summary>
    Task<IReadOnlyList<Guid>> ListFollowedAsync(Guid followerId, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if already following.</summary>
    Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if not following.</summary>
    Task<bool> RemoveFollowAsync(Guid followerId, Guid followeeId, CancellationToken cancellationToken);
}

public static class ProfileErrorCodes
{
    public const string AgentsCannotFollow = "agents_cannot_follow";
    public const string CanOnlyFollowHumans = "can_only_follow_humans";
    public const string CannotFollowSelf = "cannot_follow_self";
}
