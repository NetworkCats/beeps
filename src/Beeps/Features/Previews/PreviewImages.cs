using System.Globalization;
using Beeps.Features.Beeps;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Site;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Previews;

/// <summary>
/// The addresses of the preview images (<see cref="PreviewEndpoints"/>) that pages name as their <c>og:image</c>:
/// absolute, and in the page's language. Each ends in its version, a hash of what its image shows, so a change gives it
/// a new address, which the sites that keep a copy fetch anew; the endpoints keep each version drawn by the same hash.
/// </summary>
public sealed class PreviewImages(IOptions<SiteOptions> site, IStringLocalizer<SharedResource> localizer)
{
    /// <summary>Changed with the images' design, so every image gets a new address, and is drawn anew.</summary>
    private const string Design = "1";

    public Uri Site() => Address(PreviewEndpoints.SitePath, SiteVersion());

    public Uri Beep(BeepView beep)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return Address(PreviewEndpoints.BeepPath(beep.Id), Version(beep));
    }

    public Uri Profile(ProfileView profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Address(PreviewEndpoints.ProfilePath(profile.Actor.Id), Version(profile));
    }

    /// <summary>The site's preview, in the current language.</summary>
    public string SiteVersion() => Hash(localizer["Site.Name"], localizer["Site.Tagline"]);

    public static string Version(BeepView beep)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return Hash([beep.Text, .. Shows(beep.Author), PreviewEndpoints.ImageOf(beep)?.AbsoluteUri]);
    }

    public static string Version(ProfileView profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Hash([.. Shows(profile.Actor), profile.Bio, profile.BannerUrl?.AbsoluteUri]);
    }

    private static string?[] Shows(ActorView actor) => [actor.DisplayName, actor.Handle, actor.Kind.ToString(), actor.AvatarUrl.AbsoluteUri];

    private static string Hash(params IEnumerable<string?> shown) => TextHashes.Sha256Hex(string.Join('\0', shown.Prepend(Design)))[..16];

    private Uri Address(string path, string version) =>
        site.Value.Absolute($"{PreviewEndpoints.Prefix}/{CultureInfo.CurrentUICulture.Name}{path}?v={version}");
}
