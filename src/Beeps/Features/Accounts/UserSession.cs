using System.Buffers.Text;
using System.Security.Cryptography;
using Beeps.Features.Shared;

namespace Beeps.Features.Accounts;

/// <summary>
/// A signed-in browser (server-side session). PostgreSQL holds the record; Valkey only caches it. The
/// cookie carries just the token, and only its hash is stored, so a database leak cannot be replayed.
/// </summary>
public sealed class UserSession
{
    public const int UserAgentMaxLength = 256;
    public const int IpAddressMaxLength = 64;

    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Slides forward while the session is used (see <see cref="SessionService.Lifetime"/>).</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Recorded at sign-in, so the owner can later tell their devices apart.</summary>
    public string? UserAgent { get; init; }

    public string? IpAddress { get; init; }
}

public static class SessionTokens
{
    private const int TokenBytes = 32;

    private static readonly int TokenLength = Base64Url.GetEncodedLength(TokenBytes);

    /// <summary>256 random bits, base64url (43 characters).</summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static bool IsWellFormed(string? token) =>
        token?.Length == TokenLength && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Hash(string token) => TextHashes.Sha256Hex(token);
}
