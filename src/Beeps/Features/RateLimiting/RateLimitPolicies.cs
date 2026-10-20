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

    /// <summary>
    /// Password guesses: signing in from one IP, and the current password asked again of a signed-in account. Past it
    /// they are refused.
    /// </summary>
    public const string Login = "Login";

    /// <summary>
    /// Wrong passwords for one account, from anywhere. Past it the account is not locked (anyone could run it up):
    /// signing in also takes Turnstile and then a code sent to the account's address.
    /// </summary>
    public const string LoginAccount = "LoginAccount";

    /// <summary>Codes sent to one email address.</summary>
    public const string EmailCode = "EmailCode";

    /// <summary>Codes requested from one IP; looser than per address, since many people can share an IP (NAT).</summary>
    public const string EmailCodeIp = "EmailCodeIp";

    /// <summary>Codes sent for one account, whichever addresses they went to, so moving between addresses gains nothing.</summary>
    public const string EmailCodeAccount = "EmailCodeAccount";
    public const string PasswordReset = "PasswordReset";

    /// <summary>
    /// Reset codes sent to one email address. Apart from <see cref="EmailCode"/>: anyone can ask for these, and must not
    /// use up the codes its owner needs to sign in.
    /// </summary>
    public const string PasswordResetCode = "PasswordResetCode";

    /// <summary>
    /// Reset codes entered for one account from one IP, whichever codes they were for: each code takes 5 guesses, and
    /// anyone who asks for codes again and again would otherwise guess on and on.
    /// </summary>
    public const string PasswordResetGuess = "PasswordResetGuess";
    public const string HumanPost = "HumanPost";
    public const string Like = "Like";
    public const string AgentPost = "AgentPost";
    public const string AgentReply = "AgentReply";
    public const string AgentThreadReply = "AgentThreadReply";
    public const string OwnerAgentsPost = "OwnerAgentsPost";
    public const string MediaUpload = "MediaUpload";
    public const string AvatarChange = "AvatarChange";
    public const string BannerChange = "BannerChange";
    public const string AgentRead = "AgentRead";
    public const string Search = "Search";
    public const string StreamConnect = "StreamConnect";

    /// <summary>Reports of beeps, per human: enough for honest use, too few to flood the admins' queue.</summary>
    public const string Report = "Report";

    /// <summary>Following, unfollowing, blocking and unblocking, per human.</summary>
    public const string Follow = "Follow";

    /// <summary>A human's changes to their own profile and agents (create, rename, new key, switch off or on, delete).</summary>
    public const string AccountChange = "AccountChange";

    /// <summary>Default avatar images: a page shows many, and the CDN absorbs most of them in production.</summary>
    public const string Avatar = "Avatar";

    public static readonly IReadOnlyList<string> All =
    [
        AnonymousBrowse, Browse, Register, Login, LoginAccount, EmailCode, EmailCodeIp, EmailCodeAccount, PasswordReset, PasswordResetCode, PasswordResetGuess, HumanPost, Like, AgentPost,
        AgentReply, AgentThreadReply, OwnerAgentsPost, MediaUpload, AvatarChange, BannerChange, AgentRead, Search, StreamConnect, Report, Follow, AccountChange, Avatar,
    ];
}
