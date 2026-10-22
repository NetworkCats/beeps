using Beeps.Features.Shared;

namespace Beeps.Features.Translation;

/// <summary>
/// AI translation of beeps, by the model that reviews them (<see cref="Moderation.ModerationOptions.Model"/>), in a
/// queue of its own.
/// </summary>
public sealed class TranslationOptions : BeepQueueOptions
{
    public const string SectionName = "Translation";

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
}
