namespace Beeps.Features.RateLimiting;

/// <summary>
/// The same fixed-window counters as <see cref="IRateLimitStore"/>, kept in this instance only: what
/// <see cref="RateLimitService"/> counts with while Valkey is unavailable. Each instance then allows the full limit on
/// its own, which is looser than the shared count but still a limit; nothing is shared or kept once Valkey is back.
/// </summary>
public sealed class LocalRateLimiter(TimeProvider time)
{
    /// <summary>Counters held at most; expired ones are dropped first, and past this requests go uncounted.</summary>
    public const int MaxCounters = 100_000;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Window> counters = new(StringComparer.Ordinal);

    /// <inheritdoc cref="IRateLimitStore.WaitAsync"/>
    public TimeSpan Wait(IReadOnlyList<RateLimitCounter> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        var now = time.GetUtcNow();
        lock (gate)
        {
            return WaitLocked(wanted, now);
        }
    }

    /// <inheritdoc cref="IRateLimitStore.TryAcquireAsync"/>
    public RateLimitDecision TryAcquire(IReadOnlyList<RateLimitCounter> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        var now = time.GetUtcNow();
        lock (gate)
        {
            var wait = WaitLocked(wanted, now);
            if (wait > TimeSpan.Zero)
            {
                return RateLimitDecision.Denied(wait);
            }

            if (counters.Count + wanted.Count > MaxCounters)
            {
                foreach (var expired in counters.Where(c => c.Value.ResetAt <= now).Select(c => c.Key).ToList())
                {
                    counters.Remove(expired);
                }

                if (counters.Count + wanted.Count > MaxCounters)
                {
                    return RateLimitDecision.Allowed;
                }
            }

            foreach (var counter in wanted)
            {
                counters[counter.Key] = counters.TryGetValue(counter.Key, out var window) && window.ResetAt > now
                    ? window with { Count = window.Count + 1 }
                    : new Window(1, now + counter.Window);
            }

            return RateLimitDecision.Allowed;
        }
    }

    // The caller holds the lock.
    private TimeSpan WaitLocked(IReadOnlyList<RateLimitCounter> wanted, DateTimeOffset now)
    {
        var wait = TimeSpan.Zero;
        foreach (var counter in wanted)
        {
            if (counters.TryGetValue(counter.Key, out var window) && window.ResetAt > now && window.Count >= counter.PermitLimit && window.ResetAt - now > wait)
            {
                wait = window.ResetAt - now;
            }
        }

        return wait;
    }

    private readonly record struct Window(int Count, DateTimeOffset ResetAt);
}
