using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Live;

public sealed class LiveOptions
{
    public const string SectionName = "Live";

    /// <summary>Whether this instance reads the event stream and publishes merged counts and the admin figures (AdminFiguresWorker). Events are still published when false.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Past this, the oldest connection of a human, or of an agent (which has its own), is closed.</summary>
    [Range(1, 100)]
    public int MaxConnectionsPerActor { get; set; } = 10;

    /// <summary>Cloudflare drops connections idle for longer than about 100 seconds.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Like and reply counts are merged and pushed at most this often.</summary>
    public TimeSpan CountsInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>While an admin page is open, how often the figures that change with no event are looked at.</summary>
    public TimeSpan AdminFiguresInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How often each instance reads the stream when no instance said it added an event. Every event is announced on
    /// Pub/Sub and read at once; this only bounds the delay when such a word is lost (Valkey reconnecting).
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The stream keeps about this many recent events for replay.</summary>
    [Range(100, 1_000_000)]
    public int StreamMaxLength { get; set; } = 10_000;

    /// <summary>A reconnecting reader further behind than this catches up by fetching instead of by replay.</summary>
    public TimeSpan ReplayWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Same as <see cref="ReplayWindow"/>, counted in events.</summary>
    [Range(1, 100_000)]
    public int MaxReplay { get; set; } = 1000;

    /// <summary>
    /// How often an open stream looks again at the session or agent key it was opened with: the event that ends one may
    /// have been lost (Valkey down as it was published), and a session runs out with no event at all.
    /// </summary>
    public TimeSpan SessionCheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Told to browsers as the SSE <c>retry</c> field.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);
}
