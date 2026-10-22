using Beeps.Features.Shared;

namespace Beeps.Infrastructure;

/// <summary>How the outbox, the review and translation queues and the scheduled jobs record a failed attempt and space out the next one.</summary>
public static class FailedAttempts
{
    /// <summary>The longest error kept, and the length of the review, translation and scheduled-job error columns.</summary>
    public const int ErrorMaxLength = 2000;

    /// <summary>The exception as stored for the admins, cut to fit its column.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return TextRules.Truncate($"{exception.GetType().Name}: {exception.Message}", ErrorMaxLength);
    }

    /// <summary>Exponential backoff: base, 2×base, 4×base… capped at the maximum.</summary>
    public static TimeSpan RetryDelay(int attempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var delay = baseDelay * Math.Pow(2, exponent);
        return delay < maxDelay ? delay : maxDelay;
    }
}
