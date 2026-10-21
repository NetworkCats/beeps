using System.Net;
using System.Net.Sockets;

namespace Beeps.Features.RateLimiting;

/// <summary>
/// What requests are counted under. Each kind has its own prefix, so an id, an address and a typed name never share a
/// counter by accident; the policy is part of the key too, so one partition may serve several policies.
/// </summary>
public static class RateLimitPartitions
{
    /// <summary>A human or an agent, by its own id.</summary>
    public static string Actor(Guid actorId) => $"actor:{actorId:D}";

    /// <summary>
    /// A signed-in principal: a human's account, or an agent by its own id (what its claims name). Sign-in attempts
    /// are counted under the human's account whichever way they named it.
    /// </summary>
    public static string Account(Guid accountId) => $"account:{accountId:D}";

    /// <summary>One account as named from one IP (<see cref="Ip"/>): what others do to the account counts apart from what its owner does.</summary>
    public static string AccountFromIp(Guid accountId, string? clientIp) => $"{Account(accountId)}:{Ip(clientIp)}";

    /// <summary>All agents of one owner together.</summary>
    public static string Owner(Guid ownerId) => $"owner:{ownerId:D}";

    /// <summary>One agent's replies within one thread.</summary>
    public static string Thread(Guid actorId, Guid rootId) => $"{Actor(actorId)}:thread:{rootId:D}";

    /// <summary>
    /// An IPv4 address, or an IPv6 address's /64: one host is usually given a whole /64, and would otherwise have a
    /// fresh counter for every address it picks from it.
    /// </summary>
    public static string Ip(string? clientIp)
    {
        if (!IPAddress.TryParse(clientIp, out var address))
        {
            return $"ip:{clientIp}";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return $"ip:{address}";
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"ip:{new IPAddress(bytes)}/64";
    }

    public static string Email(string normalizedEmail) => $"email:{normalizedEmail}";

    /// <summary>One address as named from one IP (<see cref="Ip"/>): what others ask for it counts apart from what its owner asks.</summary>
    public static string EmailFromIp(string normalizedEmail, string? clientIp) => $"{Email(normalizedEmail)}:{Ip(clientIp)}";

    /// <summary>A sign-in to a name or address no account has, so guessing at unknown ones is counted as well.</summary>
    public static string Login(string normalizedLogin) => $"login:{normalizedLogin}";
}
