using Beeps.Features.RateLimiting;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.RateLimiting;

public sealed class LocalRateLimiterTests
{
    private readonly FixedTimeProvider time = new(TestData.Now);

    [Fact]
    public void TheWaitIsReadWithoutCounting()
    {
        var limiter = new LocalRateLimiter(time);
        RateLimitCounter[] counter = [new("StreamConnect:60:a", 1, TimeSpan.FromMinutes(1))];

        var before = limiter.Wait(counter);
        limiter.TryAcquire(counter);
        time.Now += TimeSpan.FromSeconds(15);
        var full = limiter.Wait(counter);
        var again = limiter.Wait(counter);

        Assert.Equal(TimeSpan.Zero, before);
        Assert.Equal(TimeSpan.FromSeconds(45), full);
        Assert.Equal(full, again);
    }

    [Fact]
    public void AReleasedHitNoLongerCounts()
    {
        var limiter = new LocalRateLimiter(time);
        RateLimitCounter[] counter = [new("LoginAccount:900:a", 1, TimeSpan.FromMinutes(15))];

        limiter.TryAcquire(counter);
        limiter.Release(counter);
        limiter.Release(counter);
        var allowed = limiter.TryAcquire(counter);
        var denied = limiter.TryAcquire(counter);

        // Never below none: released twice, it still allows one.
        Assert.True(allowed.IsAllowed);
        Assert.False(denied.IsAllowed);
    }

    [Fact]
    public void CountsEachWindowAndResetsAfterIt()
    {
        var limiter = new LocalRateLimiter(time);
        RateLimitCounter[] counter = [new("Login:60:a", 2, TimeSpan.FromMinutes(1))];

        Assert.True(limiter.TryAcquire(counter).IsAllowed);
        Assert.True(limiter.TryAcquire(counter).IsAllowed);
        time.Now += TimeSpan.FromSeconds(20);
        var denied = limiter.TryAcquire(counter);
        time.Now += TimeSpan.FromSeconds(40);

        Assert.False(denied.IsAllowed);
        Assert.Equal(TimeSpan.FromSeconds(40), denied.RetryAfter);
        Assert.True(limiter.TryAcquire(counter).IsAllowed);
    }

    [Fact]
    public void EitherAllCountersCountOrNone()
    {
        var limiter = new LocalRateLimiter(time);
        RateLimitCounter own = new("AgentPost:3600:a", 5, TimeSpan.FromHours(1));
        RateLimitCounter owner = new("OwnerAgentsPost:3600:o", 1, TimeSpan.FromHours(1));

        Assert.True(limiter.TryAcquire([own, owner]).IsAllowed);
        Assert.False(limiter.TryAcquire([own, owner]).IsAllowed);

        // The rejected request did not count against the agent's own quota.
        for (var i = 0; i < 4; i++)
        {
            Assert.True(limiter.TryAcquire([own]).IsAllowed);
        }

        Assert.False(limiter.TryAcquire([own]).IsAllowed);
    }
}
