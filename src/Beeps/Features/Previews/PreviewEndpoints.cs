using System.Globalization;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Localization;
using Beeps.Features.Media;
using Beeps.Features.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Localization;

namespace Beeps.Features.Previews;

/// <summary>
/// The preview images (<see cref="IPreviewRenderer"/>) of what signed-out visitors can read: a beep, a profile, and the
/// site for its other pages. The language is in the address, as the page that names it was drawn in
/// (<see cref="PreviewImages"/>); what is shown is as it is now. Each version is drawn the first time it is asked for
/// and kept on disk (<see cref="IPreviewStore"/>), and a beep or profile that is gone or hidden has its own deleted
/// when next asked for. On the way, a copy is kept an hour.
/// </summary>
public static class PreviewEndpoints
{
    public const string Prefix = "/previews";

    public const string SitePath = "/site.png";

    /// <summary>The kinds of preview (<see cref="PreviewName.Kind"/>), as the addresses name them.</summary>
    public const string Beeps = "beeps";

    public const string Actors = "actors";

    public const string Site = "site";

    private const string ContentType = "image/png";

    private static readonly string[] Methods = [HttpMethods.Get, HttpMethods.Head];

    public static string BeepPath(Guid beepId) => $"/{Beeps}/{beepId:D}.png";

    public static string ProfilePath(Guid actorId) => $"/{Actors}/{actorId:D}.png";

    /// <summary>The image a beep's preview shows beside it: its first, as the thumbnail the timeline shows.</summary>
    public static Uri? ImageOf(BeepView beep)
    {
        ArgumentNullException.ThrowIfNull(beep);
        return beep.Media.FirstOrDefault(m => m.Status == MediaStatus.Ready)?.ThumbnailUrl;
    }

    public static IEndpointRouteBuilder MapPreviewEndpoints(this IEndpointRouteBuilder routes)
    {
        var previews = routes.MapGroup(Prefix + "/{culture}").ExcludeFromDescription();
        previews.MapMethods(SitePath, Methods, SiteAsync);
        previews.MapMethods($"/{Beeps}/{{id:guid}}.png", Methods, BeepAsync);
        previews.MapMethods($"/{Actors}/{{id:guid}}.png", Methods, ProfileAsync);
        return routes;
    }

    private static async Task<Results<FileStreamHttpResult, FileContentHttpResult, NotFound>> SiteAsync(
        string culture,
        PreviewImages previews,
        IPreviewStore store,
        IPreviewRenderer renderer,
        IStringLocalizer<SharedResource> localizer,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UseCulture(culture) is not { } name)
        {
            return TypedResults.NotFound();
        }

        var preview = new PreviewName(Site, Site, name, previews.SiteVersion());
        return await ServeAsync(store, preview, response, () => Task.FromResult(renderer.Render(new SitePreview(localizer["Site.Name"], localizer["Site.Tagline"]))), cancellationToken);
    }

    private static async Task<Results<FileStreamHttpResult, FileContentHttpResult, NotFound>> BeepAsync(
        string culture,
        Guid id,
        BeepService beeps,
        MediaReader media,
        IPreviewStore store,
        IPreviewRenderer renderer,
        IStringLocalizer<SharedResource> localizer,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UseCulture(culture) is not { } name)
        {
            return TypedResults.NotFound();
        }

        if (await beeps.GetAsync(null, id, cancellationToken) is not { IsSuccess: true, Value: var beep })
        {
            store.Remove(Beeps, $"{id:D}");
            return TypedResults.NotFound();
        }

        var preview = new PreviewName(Beeps, $"{id:D}", name, PreviewImages.Version(beep));
        return await ServeAsync(store, preview, response, async () =>
        {
            var author = await ActorAsync(beep.Author, media, localizer, cancellationToken);
            var image = ImageOf(beep) is { } url ? await media.ReadAsync(url, cancellationToken) : null;
            var date = beep.CreatedAt.UtcDateTime.ToString(localizer["Preview.DateFormat"], CultureInfo.CurrentCulture);
            var text = BeepText.Shown(beep).Select(run => new PreviewSpan(run.Text, run.Linked)).ToList();
            return renderer.Render(new BeepPreview(author, text, image, date));
        }, cancellationToken);
    }

    private static async Task<Results<FileStreamHttpResult, FileContentHttpResult, NotFound>> ProfileAsync(
        string culture,
        Guid id,
        ProfileService profiles,
        MediaReader media,
        IPreviewStore store,
        IPreviewRenderer renderer,
        IStringLocalizer<SharedResource> localizer,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (UseCulture(culture) is not { } name)
        {
            return TypedResults.NotFound();
        }

        if (await profiles.FindHandleAsync(id, cancellationToken) is not { Length: > 0 } handle
            || await profiles.GetAsync(null, handle, cancellationToken) is not { IsSuccess: true, Value: var profile })
        {
            store.Remove(Actors, $"{id:D}");
            return TypedResults.NotFound();
        }

        var preview = new PreviewName(Actors, $"{id:D}", name, PreviewImages.Version(profile));
        return await ServeAsync(store, preview, response, async () =>
        {
            var actor = await ActorAsync(profile.Actor, media, localizer, cancellationToken);
            var banner = profile.BannerUrl is { } url ? await media.ReadAsync(url, cancellationToken) : null;
            return renderer.Render(new ProfilePreview(actor, profile.Bio, banner));
        }, cancellationToken);
    }

    /// <summary>The image is drawn in the address's language, whichever the visitor's is; null for one the site has not.</summary>
    private static string? UseCulture(string culture)
    {
        if (SupportedCultures.Find(culture) is not { } name)
        {
            return null;
        }

        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return name;
    }

    /// <summary>The version kept on disk; failing that, drawn, kept and served.</summary>
    private static async Task<Results<FileStreamHttpResult, FileContentHttpResult, NotFound>> ServeAsync(
        IPreviewStore store,
        PreviewName preview,
        HttpResponse response,
        Func<Task<byte[]>> draw,
        CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "public, max-age=3600";
        // For the sites a link is shared on, not for image search.
        response.Headers["X-Robots-Tag"] = "noindex";
        if (store.Open(preview) is { } kept)
        {
            return TypedResults.Stream(kept, ContentType);
        }

        var png = await draw();
        await store.SaveAsync(preview, png, cancellationToken);
        return TypedResults.File(png, ContentType);
    }

    private static async Task<PreviewActor> ActorAsync(ActorView actor, MediaReader media, IStringLocalizer<SharedResource> localizer, CancellationToken cancellationToken)
    {
        var avatar = await media.ReadAsync(actor.AvatarUrl, cancellationToken);
        var badge = actor.Kind == ActorKind.Agent ? localizer["Beep.Agent"].Value : null;
        return new PreviewActor(actor.Id, actor.DisplayName ?? actor.Handle ?? "", actor.Handle, badge, avatar);
    }
}

/// <summary>Reads a stored image by its public address, for drawing it into a preview.</summary>
public sealed class MediaReader(MediaUrls urls, IMediaStorage storage)
{
    /// <summary>Null for an address that is not a stored object (a default avatar) or one that is gone.</summary>
    public async Task<byte[]?> ReadAsync(Uri url, CancellationToken cancellationToken) =>
        urls.ObjectName(url) is { } name ? await storage.GetAsync(name, cancellationToken) : null;
}
