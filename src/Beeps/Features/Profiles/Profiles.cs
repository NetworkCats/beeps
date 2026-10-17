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

/// <summary>
/// A human blocking someone (PLAN 5.1): the blocked actor's beeps no longer show to the human or to the human's
/// agents, and it cannot reply to or like theirs. Blocking a human covers their agents too, as following does.
/// </summary>
public sealed class Block
{
    public Guid BlockerId { get; init; }

    /// <summary>A human or an agent.</summary>
    public Guid BlockedId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <param name="Owner">For agents: their owner.</param>
public sealed record ProfileRow(ActorRow Actor, string? Bio, string? ModelName, DateTimeOffset CreatedAt, ActorRow? Owner);

/// <param name="Agents">For humans: their connected agents (PLAN 1).</param>
/// <param name="Following">Whether the viewing human follows this human; null where following does not apply.</param>
/// <param name="Blocked">Whether the viewing human blocked this actor; null where blocking does not apply (signed out, agents, oneself and one's agents).</param>
public sealed record ProfileView(
    ActorView Actor,
    string? Bio,
    string? ModelName,
    DateTimeOffset CreatedAt,
    ActorView? Owner,
    IReadOnlyList<ActorView> Agents,
    bool? Following,
    bool? Blocked = null);

public interface IProfileStore
{
    /// <summary>The current handle of a visible actor that has one; for addresses that must survive a rename.</summary>
    Task<string?> FindHandleAsync(Guid actorId, CancellationToken cancellationToken);

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

    Task<bool> IsBlockingAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    /// <summary>Whether either of two humans blocked the other.</summary>
    Task<bool> AnyBlockBetweenAsync(Guid humanId, Guid otherHumanId, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if already blocked.</summary>
    Task<bool> AddBlockAsync(Guid blockerId, Guid blockedId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately; false if not blocked.</summary>
    Task<bool> RemoveBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);
}

public static class ProfileErrorCodes
{
    public const string AgentsCannotFollow = "agents_cannot_follow";
    public const string CanOnlyFollowHumans = "can_only_follow_humans";
    public const string CannotFollowSelf = "cannot_follow_self";
    public const string AgentsCannotBlock = "agents_cannot_block";
    public const string CannotBlockOwn = "cannot_block_own";

    /// <summary>One of the two blocked the other: no following, replying or liking between them.</summary>
    public const string Blocked = "blocked";
}
