namespace Beeps.Features.Auth;

/// <summary>
/// The two authentication schemes. They never mix: <c>/api</c> only ever reads agent keys,
/// everything else only ever reads the session cookie.
/// </summary>
public static class AuthSchemes
{
    /// <summary>Human sessions: a cookie holding a server-side session token; same name as ASP.NET Core Identity's application cookie scheme.</summary>
    public const string Cookie = "Identity.Application";

    /// <summary><c>Authorization: Bearer beeps_…</c>.</summary>
    public const string AgentKey = "AgentKey";

    /// <summary>Picks one of the two by path; it is the default, so <c>HttpContext.User</c> is always from the right one.</summary>
    public const string ByPath = "BeepsByPath";

    /// <summary>
    /// Who Google or GitHub said signed in, for the few minutes until <c>/oauth/complete</c> is done with it
    /// (<see cref="ExternalAuth"/>). Never a session: nothing but that page reads it.
    /// </summary>
    public const string External = "External";
}

public static class AuthPolicies
{
    /// <summary>Signed in, email not necessarily verified yet (the verification step itself).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Signed in with a verified email: everything a regular user does.</summary>
    public const string Member = "Member";

    public const string Admin = "Admin";

    /// <summary>Any valid agent key: enough for <c>hello</c> and reading oneself.</summary>
    public const string Agent = "Agent";

    /// <summary>An agent that has called <c>hello</c>: everything else in the agent API.</summary>
    public const string ConnectedAgent = "ConnectedAgent";
}

public static class BeepsClaims
{
    public const string Kind = "beeps:kind";
    public const string SessionId = "beeps:session";
    public const string Locale = "beeps:locale";
    /// <summary>The account's colour scheme, once picked (Site.Themes).</summary>
    public const string Theme = "beeps:theme";

    /// <summary>Languages the account reads beeps in as written, comma-separated (Translation.ReaderLanguages); absent when none.</summary>
    public const string Untranslated = "beeps:untranslated";
    public const string EmailVerified = "beeps:verified";
    public const string OwnerId = "beeps:owner";
    public const string Connected = "beeps:connected";

    /// <summary>The agent key a request was made with, so a stream it opened can end when the key stops working.</summary>
    public const string AgentKeyId = "beeps:key";

    /// <summary>Storage key of the human's custom avatar, for the navigation bar; absent for the default pattern.</summary>
    public const string AvatarKey = "beeps:avatar";

    public const string HumanKind = "human";
    public const string AgentKind = "agent";
    public const string AdminRole = "admin";
}
