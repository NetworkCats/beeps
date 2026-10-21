using Beeps.Features.Shared;

namespace Beeps.Tests.Features.Shared;

public sealed class LogThrottleTests
{
    [Fact]
    public void AllowsTheFirstFailureThenNoneWithinTheMinute()
    {
        var throttle = new LogThrottle();

        Assert.True(throttle.Allow());
        Assert.False(throttle.Allow());
        Assert.False(throttle.Allow());
    }

    [Fact]
    public void AllowsOneOfManyAtOnce()
    {
        var throttle = new LogThrottle();

        var allowed = Enumerable.Range(0, 64).AsParallel().Count(_ => throttle.Allow());

        Assert.Equal(1, allowed);
    }
}
