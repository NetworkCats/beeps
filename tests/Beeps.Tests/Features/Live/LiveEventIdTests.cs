using Beeps.Features.Live;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Live;

public sealed class LiveEventIdTests
{
    [Fact]
    public void IdsParseAndOrderByTimeThenSequence()
    {
        var now = TestData.Now.ToUnixTimeMilliseconds();
        var a = LiveEventId.Parse($"{now}-0");
        var b = LiveEventId.Parse($"{now}-12");
        var c = LiveEventId.Parse($"{now + 1}-0");

        Assert.True(a < b && b < c);
        Assert.Equal($"{now}-12", b.ToString());
        Assert.Equal(TestData.Now, a.Time);
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
