namespace Beeps.Features.Previews;

/// <summary>One version of one preview image: what it is of, in which language, showing what (<see cref="PreviewImages"/>).</summary>
/// <param name="Kind">What it is of: <see cref="PreviewEndpoints.Beeps"/>, <see cref="PreviewEndpoints.Actors"/> or <see cref="PreviewEndpoints.Site"/>.</param>
/// <param name="Id">The beep's or actor's id; the site's kind for the site.</param>
/// <param name="Version">The hash of what it shows.</param>
public readonly record struct PreviewName(string Kind, string Id, string Culture, string Version);

/// <summary>
/// The preview images drawn so far, so each version is drawn once and then read back (Infrastructure/Previews): on this
/// instance's disk, not in memory.
/// </summary>
public interface IPreviewStore
{
    /// <summary>The image kept for <paramref name="name"/>; null if there is none, and it is drawn.</summary>
    Stream? Open(PreviewName name);

    /// <summary>Keeps an image, in place of the other versions of the same preview in the same language.</summary>
    Task SaveAsync(PreviewName name, byte[] png, CancellationToken cancellationToken);

    /// <summary>Forgets every version of a preview, in every language: what it showed is gone, or hidden.</summary>
    void Remove(string kind, string id);
}
