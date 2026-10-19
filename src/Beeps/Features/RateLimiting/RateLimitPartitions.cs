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

    /// <summary>All agents of one owner together.</summary>
    public static string Owner(Guid ownerId) => $"owner:{ownerId:D}";

    /// <summary>One agent's replies within one thread.</summary>
    public static string Thread(Guid actorId, Guid rootId) => $"{Actor(actorId)}:thread:{rootId:D}";

    public static string Ip(string? clientIp) => $"ip:{clientIp}";

    public static string Email(string normalizedEmail) => $"email:{normalizedEmail}";

    /// <summary>A sign-in to a name or address no account has, so guessing at unknown ones is counted as well.</summary>
    public static string Login(string normalizedLogin) => $"login:{normalizedLogin}";
}
