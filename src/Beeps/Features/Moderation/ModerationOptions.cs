using System.ComponentModel.DataAnnotations;
using Beeps.Features.Shared;

namespace Beeps.Features.Moderation;

/// <summary>AI moderation.</summary>
public sealed class ModerationOptions : BeepQueueOptions
{
    public const string SectionName = "Moderation";

    public const string DefaultModel = "google/gemma-4-26b-a4b-it";

    /// <summary>
    /// On in production. Off for local development (<c>appsettings.Development.json</c>): new beeps are then
    /// stored as allowed and never queued for review.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The OpenRouter model id; translation (Translation.TranslationOptions) asks the same one.</summary>
    [Required]
    public string Model { get; set; } = DefaultModel;
}
