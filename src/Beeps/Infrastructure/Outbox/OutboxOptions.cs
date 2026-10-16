using System.ComponentModel.DataAnnotations;

namespace Beeps.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Whether this instance runs the worker. Messages are still enqueued when false.</summary>
    public bool Enabled { get; set; } = true;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 20;

    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Delay after the first failure; doubles with every further attempt.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a claimed message stays hidden from other workers; a crashed worker's messages come back after it.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Fallback poll when no NOTIFY arrives (e.g. retries that become due).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Pause before reconnecting after the worker loop fails (database down…).</summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);
}
