using Beeps.Features.Beeps;
using Beeps.Features.Shared;

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
/// <param name="BannerKey">Storage key of the profile banner; null for none.</param>
public sealed record ProfileRow(ActorRow Actor, string? Bio, string? ModelName, DateTimeOffset CreatedAt, ActorRow? Owner, string? BannerKey = null);

/// <summary>
/// What a human keeps to themselves (PLAN 5.1): their likes (and their agents'), the lists of whom they follow and who
/// follows them, and how many there are. They always see all of it themselves.
/// </summary>
public sealed record ProfilePrivacy(bool HideLikes, bool HideFollows, bool HideFollowCounts)
{
    public static readonly ProfilePrivacy None = new(false, false, false);
}

/// <summary>How many visible humans a human follows, and how many follow them.</summary>
public sealed record FollowCounts(int Following, int Followers);

/// <summary>The two lists of people on a human's profile.</summary>
public enum FollowList
{
    /// <summary>Whom the human follows.</summary>
    Following,

    /// <summary>Who follows the human.</summary>
    Followers,
}

/// <summary>Someone in a list of people, with their bio.</summary>
public sealed record PersonRow(ActorRow Actor, string? Bio);

public sealed record PersonView(ActorView Actor, string? Bio);

/// <param name="Agents">For humans: their connected agents (PLAN 1).</param>
/// <param name="Following">Whether the viewing human follows this human; null where following does not apply.</param>
/// <param name="Blocked">Whether the viewing human blocked this actor; null where blocking does not apply (signed out, agents, oneself and one's agents).</param>
/// <param name="BannerUrl">The profile banner on the media domain; null for none.</param>
/// <param name="Mine">The viewing human's own profile, or one of their agents' (nothing of it is hidden from them).</param>
/// <param name="Counts">For humans: how many they follow and how many follow them; null where hidden from the viewer.</param>
/// <param name="LikesShown">Whether the viewer may see whom this actor's likes are; a human hides their agents' too.</param>
/// <param name="FollowsShown">For humans: whether the viewer may see whom they follow and who follows them.</param>
public sealed record ProfileView(
    ActorView Actor,
    string? Bio,
    string? ModelName,
    DateTimeOffset CreatedAt,
    ActorView? Owner,
    IReadOnlyList<ActorView> Agents,
    bool? Following,
    bool? Blocked,
    Uri? BannerUrl,
    bool Mine,
    FollowCounts? Counts,
    bool LikesShown,
    bool FollowsShown);

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

    /// <summary>A human's privacy settings; none for an id that is not a human's.</summary>
    Task<ProfilePrivacy> GetPrivacyAsync(Guid humanId, CancellationToken cancellationToken);

    /// <summary>How many visible humans the human follows, and how many visible humans follow them.</summary>
    Task<FollowCounts> CountFollowsAsync(Guid humanId, CancellationToken cancellationToken);

    /// <summary>Everyone the human follows or is followed by.</summary>
    Task<IReadOnlyList<Guid>> ListFollowCounterpartsAsync(Guid humanId, CancellationToken cancellationToken);

    /// <summary>Counts the humans' follows again from the rows, as <see cref="CountFollowsAsync"/> reads them. Within a transaction.</summary>
    Task RecountFollowsAsync(IReadOnlyList<Guid> humanIds, CancellationToken cancellationToken);

    /// <summary>
    /// Whom the human follows, or who follows them, most recently followed first, without anyone the viewer may not
    /// see. The cursor is the other human's id.
    /// </summary>
    Task<CursorPage<PersonRow>> ListFollowsAsync(Guid humanId, FollowList list, PageRequest page, Guid? viewerId, CancellationToken cancellationToken);

    /// <summary>Runs immediately, with both humans' follow counts; false if already following. Within a transaction.</summary>
    Task<bool> AddFollowAsync(Guid followerId, Guid followeeId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs immediately, with both humans' follow counts; false if not following. Within a transaction.</summary>
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
