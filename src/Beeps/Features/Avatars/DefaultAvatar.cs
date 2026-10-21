using System.Security.Cryptography;
using Jdenticon;

namespace Beeps.Features.Avatars;

/// <summary>
/// The random-looking default avatar. The pattern is a pure function of a hash of the actor
/// id, never of the email or name, so it is rendered on demand and cached by the CDN instead of stored.
/// </summary>
public static class DefaultAvatar
{
    public const string RoutePattern = "/avatars/{id:guid}.svg";

    private const int Size = 128;

    public static string PathFor(Guid actorId) => $"/avatars/{actorId:D}.svg";

    public static string RenderSvg(Guid actorId) => Icon(actorId, Size).ToSvg();

    /// <summary>The pattern at <paramref name="size"/> pixels, for drawing it other than as SVG (link previews).</summary>
    public static Identicon Icon(Guid actorId, int size)
    {
        var hash = SHA256.HashData(actorId.ToByteArray(bigEndian: true));
        var icon = Identicon.FromHash(hash, size);
        icon.Style = new IdenticonStyle { Padding = 0.12f };
        return icon;
    }
}
