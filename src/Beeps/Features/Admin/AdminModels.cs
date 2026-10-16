using Beeps.Features.Agents;

namespace Beeps.Features.Admin;

public sealed record AdminUserDetails(AdminUserRow User, Uri AvatarUrl, IReadOnlyList<AgentSummary> Agents);

/// <summary>The full code: shown exactly once, right after it is created.</summary>
public sealed record CreatedInvite(InviteRow Invite, string Code);

public static class AdminErrorCodes
{
    public const string NotAdmin = "not_admin";
    public const string CannotTargetSelf = "cannot_target_self";
    public const string LastAdmin = "last_admin";
    public const string UserBanned = "user_banned";
}
