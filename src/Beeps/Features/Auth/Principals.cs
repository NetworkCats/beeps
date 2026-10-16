using System.Globalization;
using System.Security.Claims;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;

namespace Beeps.Features.Auth;

public static class Principals
{
    public static ClaimsPrincipal ForHuman(AccountSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, session.UserId.ToString("D")),
            new(ClaimTypes.Name, session.Handle),
            new(BeepsClaims.Kind, BeepsClaims.HumanKind),
            new(BeepsClaims.SecurityStamp, session.SecurityStamp),
            new(BeepsClaims.Locale, session.Locale),
            new(BeepsClaims.EmailVerified, session.EmailVerified ? "true" : "false"),
        ];
        if (session.IsAdmin)
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
            ],
            AuthSchemes.AgentKey));
    }

    /// <summary>The signed-in human's or agent's actor id.</summary>
    public static Guid ActorId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request is not signed in."), CultureInfo.InvariantCulture);
    }

    public static bool IsHuman(this ClaimsPrincipal principal) =>
        principal?.FindFirstValue(BeepsClaims.Kind) == BeepsClaims.HumanKind;
}
