using System.Security.Cryptography;

namespace Beeps.Features.Accounts;

/// <summary>Invite codes: random, typed by people, so no look-alike characters and grouped for reading.</summary>
public static class InviteCodes
{
    public const int PrefixLength = 4;

    // Crockford-style: no I, L, O or U.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupLength = 4;
    private const int Groups = 4;

    /// <summary>A new code such as <c>7KQ2-M9XD-…</c> (80 bits).</summary>
    public static string Generate()
    {
        var raw = RandomNumberGenerator.GetString(Alphabet, GroupLength * Groups);
        return string.Join('-', Enumerable.Range(0, Groups).Select(i => raw.Substring(i * GroupLength, GroupLength)));
    }

    /// <summary>What gets hashed: case, spaces and dashes do not matter when typing a code.</summary>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return string.Concat(code.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
    }

    public static string Hash(string code) => Shared.TextHashes.Sha256Hex(Normalize(code));

    public static string Prefix(string code) => Normalize(code)[..PrefixLength];
}
