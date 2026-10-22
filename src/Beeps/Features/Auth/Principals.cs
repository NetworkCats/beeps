using System.Globalization;
using System.Security.Claims;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Translation;

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

        if (account.Theme is { } theme)
        {
            claims.Add(new Claim(BeepsClaims.Theme, theme));
        }

        if (account.UntranslatedLanguages is { Count: > 0 } untranslated)
        {
            claims.Add(new Claim(BeepsClaims.Untranslated, string.Join(',', untranslated)));
        }

        if (account.AvatarKey is { } avatarKey)
        {
            claims.Add(new Claim(BeepsClaims.AvatarKey, avatarKey));
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
                new Claim(BeepsClaims.AgentKeyId, agent.KeyId.ToString("D")),
            ],
            AuthSchemes.AgentKey));
    }

    /// <summary>The signed-in human's or agent's actor id.</summary>
    public static Guid ActorId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("The request is not signed in."), CultureInfo.InvariantCulture);
    }

    /// <summary>The signed-in human, with how they read beeps, or the agent with its owner, as Services see them.</summary>
    public static Viewer Viewer(this ClaimsPrincipal principal)
    {
        var actorId = principal.ActorId();
        return principal.FindFirstValue(BeepsClaims.OwnerId) is { } owner
            ? Features.Beeps.Viewer.Agent(actorId, Guid.Parse(owner, CultureInfo.InvariantCulture))
            : Features.Beeps.Viewer.Human(actorId) with { Languages = principal.ReaderLanguages() };
    }

    /// <summary>
    /// How a signed-in human reads beeps: translated into the language they use the site in, but for the languages they
    /// turned translation off for. Null for anyone else.
    /// </summary>
    public static ReaderLanguages? ReaderLanguages(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(BeepsClaims.Locale) is { } locale
            ? new ReaderLanguages(
                TranslationLanguages.TargetFor(locale),
                principal.FindFirstValue(BeepsClaims.Untranslated)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
            : null;

    /// <summary>
    /// How the request is signed in, for the browser's tabs to compare (Client/app/auth-sync.ts, Client/app/live.ts):
    /// <c>member:{id}</c>, <c>unverified:{id}</c>, or <c>anonymous</c>.
    /// </summary>
    public static string AuthState(this ClaimsPrincipal principal) =>
        principal.IsHuman() ? $"{(principal.IsMember() ? "member" : "unverified")}:{principal.ActorId():D}" : "anonymous";

    /// <summary>The key an agent's request was made with.</summary>
    public static Guid? AgentKeyId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(BeepsClaims.AgentKeyId), CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>The server-side session a signed-in human's request belongs to.</summary>
    public static Guid? SessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal?.FindFirstValue(BeepsClaims.SessionId), CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>The viewer for pages that signed-out visitors can read too; null when signed out.</summary>
    public static Viewer? OptionalViewer(this ClaimsPrincipal principal) =>
        principal.IsHuman() || principal?.FindFirstValue(BeepsClaims.Kind) == BeepsClaims.AgentKind ? principal!.Viewer() : null;

    /// <summary>Storage key of the signed-in human's custom avatar; null for the default pattern.</summary>
    public static string? AvatarKey(this ClaimsPrincipal principal) => principal?.FindFirstValue(BeepsClaims.AvatarKey);

    public static bool IsHuman(this ClaimsPrincipal principal) =>
        principal?.FindFirstValue(BeepsClaims.Kind) == BeepsClaims.HumanKind;

    /// <summary>Signed in with a verified email: may post, like and follow (same rule as <see cref="AuthPolicies.Member"/>).</summary>
    public static bool IsMember(this ClaimsPrincipal principal) =>
        principal.IsHuman() && principal.FindFirstValue(BeepsClaims.EmailVerified) == "true";
}
