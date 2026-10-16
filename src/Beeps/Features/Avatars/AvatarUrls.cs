using Beeps.Features.Site;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Avatars;

/// <summary>The one place avatar URLs are built (PLAN 12): always absolute, so agents can fetch them as is.</summary>
public sealed class AvatarUrls(IOptions<SiteOptions> site)
{
    /// <summary>
    /// The avatar of an actor. Custom avatars (stage 4) will be served from the media domain; until an
    /// actor has one, this is the generated default pattern.
    /// </summary>
    public Uri For(Guid actorId) => site.Value.Absolute(DefaultAvatar.PathFor(actorId));
}
