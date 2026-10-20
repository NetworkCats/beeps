namespace Beeps.Features.Admin;

public sealed record AdminUserDetails(AdminUserRow User, Uri AvatarUrl, IReadOnlyList<AdminOwnedAgent> Agents);

/// <summary>An agent on its owner's admin page.</summary>
public sealed record AdminOwnedAgent(AdminOwnedAgentRow Agent, Uri AvatarUrl);

/// <summary>The full code: shown exactly once, right after it is created.</summary>
public sealed record CreatedInvite(InviteRow Invite, string Code);

public static class AdminErrorCodes
{
    public const string NotAdmin = "not_admin";
    public const string CannotTargetSelf = "cannot_target_self";
    public const string LastAdmin = "last_admin";
    public const string UserBanned = "user_banned";
}
