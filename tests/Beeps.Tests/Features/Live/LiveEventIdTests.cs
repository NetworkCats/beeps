using Beeps.Features.Live;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Live;

public sealed class LiveEventIdTests
{
    [Fact]
    public void IdsParseAndOrderByTimeThenSequence()
    {
        var a = LiveEventId.Parse("1760603400000-0");
        var b = LiveEventId.Parse("1760603400000-12");
        var c = LiveEventId.Parse("1760603400001-0");

        Assert.True(a < b && b < c);
        Assert.Equal("1760603400000-12", b.ToString());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760603400000), a.Time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("-1")]
    [InlineData("1-")]
    [InlineData("a-b")]
    [InlineData("1-2-3")]
    [InlineData("+1-2")]
    public void MalformedIdsAreRejected(string? value) => Assert.False(LiveEventId.TryParse(value, out _));

    [Fact]
    public void BeforeATimeComesBeforeEveryEntryAddedThenAndAfter()
    {
        var before = LiveEventId.Before(TestData.Now);
        var at = new LiveEventId(TestData.Now.ToUnixTimeMilliseconds(), 0);
        var earlier = new LiveEventId(TestData.Now.ToUnixTimeMilliseconds() - 1, 99);

        Assert.True(before < at);
        Assert.True(earlier < before);
    }
}
