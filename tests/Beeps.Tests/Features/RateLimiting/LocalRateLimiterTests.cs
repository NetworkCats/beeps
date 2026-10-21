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

    [Fact]
    public void AFullTableStillCountsWhatItHoldsAndRefusesNewGuessesRatherThanLetThemThrough()
    {
        var limiter = new LocalRateLimiter(time);
        RateLimitCounter account = new("LoginAccount:900:victim", 2, TimeSpan.FromMinutes(15), Guards: true);
        Assert.True(limiter.TryAcquire([account]).IsAllowed);
        for (var i = 1; i < LocalRateLimiter.MaxCounters; i++)
        {
            limiter.TryAcquire([new RateLimitCounter($"AnonymousBrowse:60:{i}", 100, TimeSpan.FromMinutes(1))]);
        }

        RateLimitCounter guess = new("Login:900:new", 5, TimeSpan.FromMinutes(15), Guards: true);
        var newGuess = limiter.TryAcquire([guess]);
        var browsing = limiter.TryAcquire([new RateLimitCounter("AnonymousBrowse:60:new", 100, TimeSpan.FromMinutes(1))]);
        var held = limiter.TryAcquire([account]);
        var past = limiter.TryAcquire([account]);

        Assert.False(newGuess.IsAllowed);
        Assert.Equal(TimeSpan.FromMinutes(1), newGuess.RetryAfter);
        Assert.True(browsing.IsAllowed);
        Assert.True(held.IsAllowed);
        Assert.False(past.IsAllowed);

        // Once the browsing counters run out there is room again.
        time.Now += TimeSpan.FromMinutes(1);
        Assert.True(limiter.TryAcquire([guess]).IsAllowed);
    }
}
