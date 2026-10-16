namespace Beeps.Features.Moderation;

/// <summary>AI moderation (PLAN 6.20).</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>
    /// On in production. Off for local development (<c>appsettings.Development.json</c>): new beeps are then
    /// stored as allowed and never queued for review.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
