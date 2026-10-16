namespace Beeps.Tests.Support;

/// <summary>A fixed "now" with real timers, for code that both reads the clock and waits (heartbeats, polling).</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
