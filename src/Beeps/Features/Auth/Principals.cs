using System.Globalization;
using System.Security.Claims;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;

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
