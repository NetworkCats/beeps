using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Beeps.Features.Auth;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Localization;

namespace Beeps.Pages.Islands;

/// <param name="Human">Signed in (verified or not); signed out, the rest is empty.</param>
public sealed record ClientViewer(bool Human, Guid Id, string Handle, Uri? Avatar, bool Member, bool Admin);

public sealed record ClientAntiforgery(string Field, string Token);

public sealed record ClientLimits(int BeepLength, int ImagesPerBeep, long MediaBytes);

/// <summary>What the app needs besides an island's props: interface text in the page's language, the antiforgery token for the forms it draws (signed in), who is signed in.</summary>
public sealed record ClientConfigProps(IReadOnlyDictionary<string, string> Strings, ClientAntiforgery Antiforgery, ClientViewer Viewer, ClientLimits Limits);

/// <summary>
/// The layout's <c>&lt;script type="application/json" id="app-config"&gt;</c> (<c>Client/app/config.ts</c>). Signed out
/// it holds nothing personal and no antiforgery token (which would set a cookie), so public pages stay cacheable. The
/// text the components show comes from the same resource files as the pages': the keys they use are
/// listed in <c>Client/strings.json</c> (embedded), so only those are sent.
/// </summary>
public sealed class ClientConfig(IStringLocalizer<SharedResource> localizer, IAntiforgery antiforgery, AvatarUrls avatars)
{
    public static readonly IReadOnlyList<string> Keys = LoadKeys();

    public string For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.User;
        var tokens = user.IsHuman() ? antiforgery.GetAndStoreTokens(context) : null;
        var strings = Keys.ToDictionary(key => key, key => localizer[key].Value, StringComparer.Ordinal);
        return IslandJson.Serialize(new ClientConfigProps(
            strings,
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

    private static List<string> LoadKeys()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beeps.client-strings.json")
            ?? throw new InvalidOperationException("Client/strings.json is not embedded.");
        return JsonSerializer.Deserialize(stream, IslandJsonContext.Default.ListString) ?? [];
    }
}
