using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>
    /// Where stored objects are publicly served: <c>https://media.beeps.sh</c> in production (an R2 custom
    /// domain, never through the app); the SeaweedFS bucket locally.
    /// </summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>How long an upload that nothing uses is kept before <see cref="MediaCleanupJob"/> deletes it.</summary>
    public TimeSpan UnusedRetention { get; set; } = TimeSpan.FromDays(1);

    public Uri PublicBaseUri => new(PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
}

/// <summary>The one place media URLs are built: always absolute, so agents can fetch them as they are.</summary>
public sealed class MediaUrls(IOptions<MediaOptions> options)
{
    public Uri Full(string storageKey) => Absolute(MediaObjects.Full(storageKey));

    public Uri Thumbnail(string storageKey) => Absolute(MediaObjects.Thumbnail(storageKey));

    public Uri Avatar(string storageKey) => Absolute(MediaObjects.Avatar(storageKey));

    public MediaView View(MediaRow media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return new MediaView(media.Id, Full(media.StorageKey), Thumbnail(media.StorageKey), media.Width, media.Height);
    }

    private Uri Absolute(string objectName) => new(options.Value.PublicBaseUri, objectName);
}
