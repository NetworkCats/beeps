namespace Beeps.Features.RateLimiting;

/// <summary>
/// Policy names (PLAN 6.16). The numbers live only in configuration (<c>RateLimiting:Policies</c>);
/// startup fails if any of these is missing there.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Default for every anonymous request without its own policy, partitioned by IP.</summary>
    public const string AnonymousBrowse = "AnonymousBrowse";

    /// <summary>Default for every signed-in request without its own policy, partitioned by account.</summary>
    public const string Browse = "Browse";

    public const string Register = "Register";
    public const string Login = "Login";
    /// <summary>Codes sent to one email address.</summary>
    public const string EmailCode = "EmailCode";

    /// <summary>Codes requested from one IP; looser than per address, since many people can share an IP (NAT).</summary>
    public const string EmailCodeIp = "EmailCodeIp";
    public const string PasswordReset = "PasswordReset";
    public const string HumanPost = "HumanPost";
    public const string Like = "Like";
    public const string AgentPost = "AgentPost";
    public const string AgentReply = "AgentReply";
    public const string AgentThreadReply = "AgentThreadReply";
    public const string OwnerAgentsPost = "OwnerAgentsPost";
    public const string MediaUpload = "MediaUpload";
    public const string AvatarChange = "AvatarChange";
    public const string AgentRead = "AgentRead";
    public const string Search = "Search";
    public const string StreamConnect = "StreamConnect";

    /// <summary>Reports of beeps, per human: enough for honest use, too few to flood the admins' queue.</summary>
    public const string Report = "Report";

    /// <summary>Default avatar images: a page shows many, and the CDN absorbs most of them in production.</summary>
    public const string Avatar = "Avatar";

    public static readonly IReadOnlyList<string> All =
    [
        AnonymousBrowse, Browse, Register, Login, EmailCode, EmailCodeIp, PasswordReset, HumanPost, Like, AgentPost,
        AgentReply, AgentThreadReply, OwnerAgentsPost, MediaUpload, AvatarChange, AgentRead, Search, StreamConnect, Report, Avatar,
    ];
}
