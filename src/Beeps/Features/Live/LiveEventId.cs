using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Beeps.Features.Live;

/// <summary>
/// A stream entry id, <c>{milliseconds}-{sequence}</c>: the time Valkey added the entry, so ids order events
/// and a page's render time can stand in for "everything after this moment".
/// </summary>
public readonly record struct LiveEventId(long Milliseconds, long Sequence) : IComparable<LiveEventId>
{
    public static readonly LiveEventId Zero = new(0, 0);

    /// <summary>Before every entry added at or after <paramref name="time"/>.</summary>
    public static LiveEventId Before(DateTimeOffset time) => new(Math.Max(0, time.ToUnixTimeMilliseconds() - 1), long.MaxValue);

    public DateTimeOffset Time => DateTimeOffset.FromUnixTimeMilliseconds(Milliseconds);

    public static bool operator <(LiveEventId left, LiveEventId right) => left.CompareTo(right) < 0;

    public static bool operator >(LiveEventId left, LiveEventId right) => left.CompareTo(right) > 0;

    public static bool operator <=(LiveEventId left, LiveEventId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(LiveEventId left, LiveEventId right) => left.CompareTo(right) >= 0;

    public static bool TryParse([NotNullWhen(true)] string? value, out LiveEventId id)
    {
        id = default;
        var dash = value?.IndexOf('-', StringComparison.Ordinal) ?? -1;
        if (dash <= 0
            || !long.TryParse(value.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || !long.TryParse(value.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
        {
            return false;
        }

        id = new LiveEventId(milliseconds, sequence);
        return true;
    }

    public static LiveEventId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"'{value}' is not a stream entry id.");

    public int CompareTo(LiveEventId other)
    {
        var byTime = Milliseconds.CompareTo(other.Milliseconds);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Milliseconds}-{Sequence}");
}
