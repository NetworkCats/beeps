using System.Globalization;
using System.Security.Claims;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;

namespace Beeps.Features.Auth;

public static class Principals
{
    /// <param name="sessionId">The server-side session, once there is one (not yet while signing in).</param>
    public static ClaimsPrincipal ForHuman(AccountState account, Guid? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, account.UserId.ToString("D")),
            new(ClaimTypes.Name, account.Handle),
            new(BeepsClaims.Kind, BeepsClaims.HumanKind),
            new(BeepsClaims.Locale, account.Locale),
            new(BeepsClaims.EmailVerified, account.EmailVerified ? "true" : "false"),
        ];
        if (sessionId is { } id)
        {
            claims.Add(new Claim(BeepsClaims.SessionId, id.ToString("D")));
        }

        if (account.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, BeepsClaims.AdminRole));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie));
    }

    public static ClaimsPrincipal ForAgent(AgentIdentity agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, agent.AgentId.ToString("D")),
                new Claim(BeepsClaims.Kind, BeepsClaims.AgentKind),
                new Claim(BeepsClaims.OwnerId, agent.OwnerId.ToString("D")),
                new Claim(BeepsClaims.Connected, agent.Connected ? "true" : "false"),
            ],
            AuthSchemes.AgentKey));
    }

    /// <summary>The signed-in human's or agent's actor id.</summary>
    public static Guid ActorId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request is not signed in."), CultureInfo.InvariantCulture);
    }

    /// <summary>The signed-in human, or the agent with its owner, as Services see them.</summary>
    public static Viewer Viewer(this ClaimsPrincipal principal)
    {
        var actorId = principal.ActorId();
        return principal.FindFirstValue(BeepsClaims.OwnerId) is { } owner
            ? Features.Beeps.Viewer.Agent(actorId, Guid.Parse(owner, CultureInfo.InvariantCulture))
            : Features.Beeps.Viewer.Human(actorId);
    }

    /// <summary>The server-side session a signed-in human's request belongs to.</summary>
    public static Guid? SessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(BeepsClaims.SessionId), CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>The viewer for pages that signed-out visitors can read too; null when signed out.</summary>
    public static Viewer? OptionalViewer(this ClaimsPrincipal principal) =>
        principal.IsHuman() || principal?.FindFirstValue(BeepsClaims.Kind) == BeepsClaims.AgentKind ? principal!.Viewer() : null;

    public static bool IsHuman(this ClaimsPrincipal principal) =>
        principal?.FindFirstValue(BeepsClaims.Kind) == BeepsClaims.HumanKind;

    /// <summary>Signed in with a verified email: may post, like and follow (same rule as <see cref="AuthPolicies.Member"/>).</summary>
    public static bool IsMember(this ClaimsPrincipal principal) =>
        principal.IsHuman() && principal.FindFirstValue(BeepsClaims.EmailVerified) == "true";
}
