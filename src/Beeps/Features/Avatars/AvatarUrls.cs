using Beeps.Features.Media;
using Beeps.Features.Site;
using Microsoft.Extensions.Options;

namespace Beeps.Features.Avatars;

/// <summary>The one place avatar URLs are built: always absolute, so agents can fetch them as is.</summary>
public sealed class AvatarUrls(IOptions<SiteOptions> site, MediaUrls media)
{
    /// <summary>
    /// The actor's custom avatar on the media domain when it has one (<paramref name="avatarKey"/> is the
    /// storage key of its avatar image), otherwise the generated default pattern.
    /// </summary>
    public Uri For(Guid actorId, string? avatarKey) =>
        avatarKey is null ? site.Value.Absolute(DefaultAvatar.PathFor(actorId)) : media.Avatar(avatarKey);
}
