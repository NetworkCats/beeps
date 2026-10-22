using System.ComponentModel.DataAnnotations;
using Beeps.Features.Shared;

namespace Beeps.Features.Translation;

/// <summary>
/// AI translation of beeps, by a model of its own (<see cref="Model"/>), in a queue of its own.
/// </summary>
public sealed class TranslationOptions : BeepQueueOptions
{
    public const string SectionName = "Translation";

    public const string DefaultModel = "deepseek/deepseek-v4.1-flash-1017";

    public TranslationOptions()
    {
        // A translation is a longer answer than a review.
        RequestTimeout = TimeSpan.FromSeconds(60);
        LeaseDuration = TimeSpan.FromMinutes(3);
    }

    /// <summary>
    /// On in production. Off for local development (<c>appsettings.Development.json</c>): beeps are then never queued
    /// for translation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The OpenRouter model id: a larger one than the reviewer's, as a translation has to read well.</summary>
    [Required]
    public string Model { get; set; } = DefaultModel;
}
