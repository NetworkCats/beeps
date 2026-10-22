using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Shared;

/// <summary>
/// A queue of work on single beeps that a model does (the review, the translation): each in its own table, taken by
/// workers on every instance and retried with backoff (Infrastructure/Queues).
/// </summary>
public abstract class BeepQueueOptions
{
    /// <summary>Jobs one instance runs at once; every instance takes its share of the queue.</summary>
    [Range(1, 64)]
    public int Concurrency { get; set; } = 8;

    /// <summary>Whether this instance runs the worker. Beeps are still queued when false.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Attempts per beep before it is given up on.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>One request to the model, end to end.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delay after the first failure; doubles with every further attempt.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a claimed job stays hidden from other workers; longer than a request can take.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Fallback poll when no NOTIFY arrives (retries that become due).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Pause before reconnecting after the worker loop fails (database down…).</summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);
}
