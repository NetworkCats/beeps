namespace Beeps.Features.RateLimiting;

/// <summary>Shared counters for rate limiting (Redis in production, so every instance sees the same counts).</summary>
public interface IRateLimitStore
{
    /// <summary>
    /// Atomically: if every counter is below its limit, counts one hit on all of them and allows;
    /// otherwise counts nothing and returns how long until the most restrictive window resets.
    /// </summary>
    Task<RateLimitDecision> TryAcquireAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken);

    /// <summary>How long until <see cref="TryAcquireAsync"/> would allow; counts nothing.</summary>
    Task<TimeSpan> WaitAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken);

    /// <summary>Takes back one hit on each counter still in its window: a request that turned out not to count.</summary>
    Task ReleaseAsync(IReadOnlyList<RateLimitCounter> counters, CancellationToken cancellationToken);
}

/// <summary>One fixed-window counter. <see cref="Key"/> is unique per policy, rule and partition.</summary>
/// <param name="Guards">Whether its policy holds back guessing (<see cref="RateLimitPolicies.Guarding"/>).</param>
public sealed record RateLimitCounter(string Key, int PermitLimit, TimeSpan Window, bool Guards = false);

public readonly record struct RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    public static RateLimitDecision Allowed { get; } = new(true, TimeSpan.Zero);

    public static RateLimitDecision Denied(TimeSpan retryAfter) => new(false, retryAfter);
}

/// <summary>A policy applied to one partition, e.g. <c>(Login, "ip:203.0.113.7")</c>.</summary>
public readonly record struct RateLimitTarget(string Policy, string Partition);
