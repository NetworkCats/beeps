using System.Security.Cryptography;
using System.Text;

namespace Beeps.Features.Shared;

/// <summary>SHA-256 for secrets that are stored only as a hash (agent keys, invite codes, email codes).</summary>
public static class TextHashes
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Compares two hex hashes without leaking where they differ through timing.</summary>
    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
