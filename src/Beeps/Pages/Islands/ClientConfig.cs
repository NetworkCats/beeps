using System.Security.Claims;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Microsoft.AspNetCore.Antiforgery;

namespace Beeps.Pages.Islands;

/// <param name="Human">Signed in (verified or not); signed out, the rest is empty.</param>
public sealed record ClientViewer(bool Human, Guid Id, string Handle, Uri? Avatar, bool Member, bool Admin);

public sealed record ClientAntiforgery(string Field, string Token);

public sealed record ClientLimits(int BeepLength, int ImagesPerBeep, long MediaBytes);

/// <summary>
/// What the app needs besides an island's props: where the interface text in the page's language is (<see cref="ClientStrings"/>),
/// the antiforgery token for the forms it draws (signed in), who is signed in.
/// </summary>
public sealed record ClientConfigProps(string StringsUrl, ClientAntiforgery Antiforgery, ClientViewer Viewer, ClientLimits Limits);

/// <summary>
/// The layout's <c>&lt;script type="application/json" id="app-config"&gt;</c> (<c>Client/app/config.ts</c>). Signed out
/// it holds nothing personal and no antiforgery token (which would set a cookie), so public pages stay cacheable. The
/// text the components show is not in it, only its address: it is the same on every page, so the app keeps it.
/// </summary>
public sealed class ClientConfig(ClientStrings strings, IAntiforgery antiforgery, AvatarUrls avatars)
{
    public string For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User;
        var tokens = user.IsHuman() ? antiforgery.GetAndStoreTokens(context) : null;
        return IslandJson.Serialize(new ClientConfigProps(
            strings.UrlFor(context),
            new ClientAntiforgery(tokens?.FormFieldName ?? "", tokens?.RequestToken ?? ""),
            Viewer(user),
            new ClientLimits(BeepText.MaxLength, MediaRules.MaxImagesPerBeep, MediaRules.MaxBytes)));
    }

    public ClientViewer Viewer(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsHuman()
            ? new ClientViewer(true, user.ActorId(), user.Identity?.Name ?? "", avatars.For(user.ActorId(), user.AvatarKey()), user.IsMember(), user.IsInRole(BeepsClaims.AdminRole))
            : new ClientViewer(false, Guid.Empty, "", null, false, false);
    }
}
