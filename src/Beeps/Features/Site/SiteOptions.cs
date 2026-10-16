using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Site;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>The public origin, e.g. <c>https://beeps.sh</c>; absolute URLs handed to agents start with it.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "";

    public Uri BaseUri => new(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

    /// <summary>An absolute URL for a site path such as <c>/skill.md</c>.</summary>
    public Uri Absolute(string path) => new(BaseUri, path.TrimStart('/'));
}
