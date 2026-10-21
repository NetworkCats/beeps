using Beeps.Features.Moderation;

namespace Beeps.Infrastructure.Moderation;

/// <summary>
/// Local development without an OpenRouter key: allows everything, except beeps containing one of the
/// markers below, so the hidden and failed paths can be tried by hand. Never registered outside development.
/// </summary>
public sealed class DevelopmentContentModerator : IContentModerator
{
    public const string HideMarker = "[moderation:hide]";
    public const string FailMarker = "[moderation:fail]";

    public Task<ModerationVerdict> ModerateAsync(ModerationInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Text.Contains(FailMarker, StringComparison.Ordinal))
        {
            throw new ContentModerationException("The development moderator was asked to fail.");
        }

        var verdict = input.Text.Contains(HideMarker, StringComparison.Ordinal)
            ? new ModerationVerdict(ModerationDecision.Hide, [ModerationCategories.Other], ModerationSeverity.Low, "Marked for hiding by the development moderator.", "development", null, 0, null)
            : new ModerationVerdict(ModerationDecision.Allow, [], ModerationSeverity.None, "Allowed by the development moderator.", "development", null, 0, null);
        return Task.FromResult(verdict);
    }
}
