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
    /// admin" (ban, delete, revoke) both read the admin set and then change it. Admin changes to agents (an owner's
    /// ban read before enabling one) and to site settings take it too, so they come before or after a ban, never between.
    /// </summary>
    public const long Admins = 1;

    /// <summary>Per-owner key for the agent limit, so two parallel creates cannot both pass the count.</summary>
    public static long OwnerAgents(Guid ownerId) => Derive("owner-agents", ownerId);

    /// <summary>
    /// Per-account key for an unverified address: verifying it and moving to another both decide from an earlier read
    /// of it, so one must not commit over the other (an address nobody verified would end up verified).
    /// </summary>
    public static long UnverifiedEmail(Guid userId) => Derive("unverified-email", userId);

    /// <summary>
    /// Per-account key for the ways to sign in: removing a passkey or unlinking Google or GitHub decides from the ones
    /// left, so two removals at once must not both pass "not the last one".
    /// </summary>
    public static long SignInMethods(Guid userId) => Derive("sign-in-methods", userId);

    /// <summary>Held by whichever instance is running the scheduled job right now.</summary>
    public static long ScheduledJob(string name) => Derive($"job:{name}");

    // Derived keys share the bigint space with the constants above; a collision only makes two
    // unrelated operations wait for each other briefly.
    private static long Derive(string scope, Guid id) => Derive($"{scope}:{id}");

    private static long Derive(string name)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
