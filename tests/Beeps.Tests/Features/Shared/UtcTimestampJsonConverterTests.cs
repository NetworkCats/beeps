using System.Text.Json;
using Beeps.Features.Shared.Http;

namespace Beeps.Tests.Features.Shared;

public sealed class UtcTimestampJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcTimestampJsonConverter() } };

    [Theory]
    [InlineData("2026-10-16T08:30:00+00:00", "\"2026-10-16T08:30:00Z\"")]
    [InlineData("2026-10-16T16:30:00+08:00", "\"2026-10-16T08:30:00Z\"")]
    [InlineData("2026-10-16T08:30:00.1234567+00:00", "\"2026-10-16T08:30:00.1234567Z\"")]
    public void WritesUtcWithZ(string value, string json) =>
        Assert.Equal(json, JsonSerializer.Serialize(DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Options));

    [Fact]
    public void ReadsAnyOffsetAsUtc()
    {
        var value = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-10-16T16:30:00+08:00\"", Options);

        Assert.Equal(TimeSpan.Zero, value.Offset);
        Assert.Equal(8, value.Hour);
    }
}
