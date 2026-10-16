namespace Beeps.Features.Admin;

/// <summary>One admin action (PLAN 3.6): who, when, on what, and what. Append-only; nothing deletes these rows.</summary>
public sealed class AdminAuditLog
{
    public const int ActionMaxLength = 64;
    public const int TargetTypeMaxLength = 32;
    public const int DetailMaxLength = 1000;

    public Guid Id { get; init; }

    public Guid AdminId { get; init; }

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public required string Action { get; init; }

    /// <summary>One of <see cref="AuditTargets"/>.</summary>
    public required string TargetType { get; init; }

    public Guid? TargetId { get; init; }

    public string? Detail { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Stable action names stored in the audit log.</summary>
public static class AuditActions
{
    public const string BanUser = "user.ban";
    public const string UnbanUser = "user.unban";
    public const string DeleteUser = "user.delete";
    public const string GrantAdmin = "user.grant_admin";
    public const string RevokeAdmin = "user.revoke_admin";
    public const string SetRegistrationMode = "site.registration_mode";
    public const string CreateInvite = "invite.create";
    public const string RevokeInvite = "invite.revoke";
}

public static class AuditTargets
{
    public const string User = "user";
    public const string Site = "site";
    public const string Invite = "invite";
}
