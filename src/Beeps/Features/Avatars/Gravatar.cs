using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Avatars;

public sealed class GravatarOptions
{
    public const string SectionName = "Gravatar";

    /// <summary>Whether <see cref="GravatarImportJob"/> looks accounts up. Off, they wait until it is on again.</summary>
    public bool Enabled { get; set; } = true;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://gravatar.com/avatar/";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public Uri BaseUri => new(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
}

/// <summary>Gravatar, asked from the server alone: no page ever links to it, so the hash of an address is never shown.</summary>
public interface IGravatar
{
    /// <summary>
    /// The image the address has on Gravatar, as Gravatar sends it; null when it has none (or one too large to take).
    /// Throws when Gravatar cannot be asked, so the address is asked again later.
    /// </summary>
    Task<byte[]?> FindAsync(string email, CancellationToken cancellationToken);
}
