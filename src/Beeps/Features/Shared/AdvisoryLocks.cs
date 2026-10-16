using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Beeps.Features.Shared;

/// <summary>
/// Keys for PostgreSQL transaction-level advisory locks. Every place that must serialize the same
/// decision across instances takes the same key, so the keys live in one list.
/// </summary>
public static class AdvisoryLocks
{
    /// <summary>
    /// Who counts as an admin: "the first user becomes admin" (registration) and "never remove the last
    /// admin" (ban, delete, revoke) both read the admin set and then change it.
    /// </summary>
    public const long Admins = 1;

    /// <summary>Per-owner key for the agent limit, so two parallel creates cannot both pass the count.</summary>
    public static long OwnerAgents(Guid ownerId) => Derive("owner-agents", ownerId);

    // Derived keys share the bigint space with the constants above; a collision only makes two
    // unrelated operations wait for each other briefly.
    private static long Derive(string scope, Guid id)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{id}"), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
