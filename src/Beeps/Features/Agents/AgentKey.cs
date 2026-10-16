using System.Buffers;
using System.Security.Cryptography;
using Beeps.Features.Shared;

namespace Beeps.Features.Agents;

/// <summary>An agent's API key (PLAN 4.1). Only the hash and a short prefix are stored.</summary>
public sealed class AgentKey
{
    public Guid Id { get; init; }

    public Guid AgentId { get; init; }

    /// <summary>SHA-256 of the full key, hex. The key has 256 bits of entropy, so no salt or slow hash is needed.</summary>
    public required string KeyHash { get; init; }

    /// <summary>E.g. <c>beeps_3fK9qA</c>: enough for the owner to recognise the key.</summary>
    public required string KeyPrefix { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}

public static class AgentKeys
{
    public const string Prefix = "beeps_";

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // 43 base-62 characters ≈ 256 bits. Letters and digits only, so a double-click selects the whole key.
    private const int RandomLength = 43;

    private const int DisplayPrefixLength = 6;

    private static readonly SearchValues<char> AlphabetValues = SearchValues.Create(Alphabet);

    public static int Length => Prefix.Length + RandomLength;

    public static string Generate() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    /// <summary>Cheap shape check before any lookup, so junk never reaches the database or cache.</summary>
    public static bool IsWellFormed(string? key) =>
        key is not null
        && key.Length == Length
        && key.StartsWith(Prefix, StringComparison.Ordinal)
        && !key.AsSpan(Prefix.Length).ContainsAnyExcept(AlphabetValues);

    public static string Hash(string key) => TextHashes.Sha256Hex(key);

    public static string DisplayPrefix(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key[..(Prefix.Length + DisplayPrefixLength)];
    }
}
