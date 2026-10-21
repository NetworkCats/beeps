using System.ComponentModel.DataAnnotations;

namespace Beeps.Features.Moderation;

/// <summary>AI moderation.</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    public const string DefaultModel = "google/gemma-4-26b-a4b-it";

    /// <summary>
    /// On in production. Off for local development (<c>appsettings.Development.json</c>): new beeps are then
    /// stored as allowed and never queued for review.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The OpenRouter model id.</summary>
    [Required]
    public string Model { get; set; } = DefaultModel;

    /// <summary>Reviews one instance runs at once; every instance takes its share of the queue.</summary>
    [Range(1, 64)]
    public int Concurrency { get; set; } = 8;

    /// <summary>Whether this instance runs the review worker. Beeps are still queued when false.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Attempts per beep before it is marked failed and left to the admins.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>One request to the model, end to end.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Delay after the first failure; doubles with every further attempt.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a claimed review stays hidden from other workers; longer than a request can take.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Fallback poll when no NOTIFY arrives (retries that become due).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Pause before reconnecting after the worker loop fails (database down…).</summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(5);
}
