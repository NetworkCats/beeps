using System.Buffers;
using System.Text.RegularExpressions;

namespace Beeps.Features.Shared;

/// <summary>
/// Links people write: those in beeps and a profile's website. They lead off the site to places no one here vouches
/// for, so they carry <see cref="Rel"/> and open in a new tab, and they show without their scheme, as on Twitter.
/// </summary>
public static partial class UserLinks
{
    /// <summary>
    /// Not followed by search engines, as written by users (<c>nofollow ugc</c>); the page opened gets neither this
    /// window (<c>noopener</c>) nor this page's address (<c>noreferrer</c>).
    /// </summary>
    public const string Rel = "nofollow noopener noreferrer ugc";

    private static readonly SearchValues<char> HostCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-");

    /// <summary>
    /// A domain name worth linking to: labels of ASCII letters, digits and hyphens between dots, at least two of them,
    /// the last with a letter (so no IP address, and no <c>localhost</c>). International names come as punycode.
    /// </summary>
    public static bool IsHost(ReadOnlySpan<char> host)
    {
        if (host.IsEmpty || host.Length > 253)
        {
            return false;
        }

        var labels = 0;
        var lastHasLetter = false;
        foreach (var range in host.Split('.'))
        {
            var label = host[range];
            if (label.IsEmpty || label.Length > 63 || label[0] == '-' || label[^1] == '-' || label.ContainsAnyExcept(HostCharacters))
            {
                return false;
            }

            labels++;
            lastHasLetter = label.IndexOfAnyInRange('a', 'z') >= 0 || label.IndexOfAnyInRange('A', 'Z') >= 0;
        }

        return labels >= 2 && lastHasLetter;
    }

    /// <summary>How much of the front of an address it shows without: <c>http://</c> or <c>https://</c>, then <c>www.</c>.</summary>
    public static int HiddenPrefixLength(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return HiddenPrefix().Match(url).Length;
    }

    /// <summary>An address as shown on its own (a profile's website): without <see cref="HiddenPrefixLength"/>, or a bare <c>/</c> at the end.</summary>
    public static string Text(string url)
    {
        var shown = url[HiddenPrefixLength(url)..];
        return shown.Count(c => c == '/') == 1 && shown.EndsWith('/') ? shown[..^1] : shown;
    }

    [GeneratedRegex("^(?:https?://)?(?:www\\.)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenPrefix();
}
