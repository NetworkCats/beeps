using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Beeps.Tests.Features.RateLimiting;

public sealed class RateLimitServiceTests
{
    private readonly IRateLimitStore store = Substitute.For<IRateLimitStore>();
    private readonly IStringLocalizer<SharedResource> localizer = Substitute.For<IStringLocalizer<SharedResource>>();
    private readonly RateLimitOptions options = new()
    {
        Policies =
        {
            ["Login"] = Policy((10, TimeSpan.FromMinutes(15))),
            ["EmailCode"] = Policy((1, TimeSpan.FromMinutes(1)), (10, TimeSpan.FromDays(1))),
        },
    };

    public RateLimitServiceTests()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Allowed);
        localizer["RateLimit.Detail"].Returns(new LocalizedString("RateLimit.Detail", "Too many requests."));
    }

    [Fact]
    public async Task AllowedWhenTheStoreAllows()
    {
        var result = await CreateService().CheckAsync("Login", "ip:203.0.113.7", TestSupport.Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task OneCounterPerRuleWithHashedPartition()
    {
        await CreateService().CheckAsync("EmailCode", "email:someone@example.com", TestSupport.Ct);

        await store.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(counters =>
                counters.Count == 2
                && counters[0].Key.StartsWith("EmailCode:60:", StringComparison.Ordinal)
                && counters[0].PermitLimit == 1
                && counters[1].Key.StartsWith("EmailCode:86400:", StringComparison.Ordinal)
                && counters[1].Window == TimeSpan.FromDays(1)
                && counters.All(c => !c.Key.Contains("example.com", StringComparison.Ordinal))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeveralTargetsAreCheckedInOneAtomicCall()
    {
        await CreateService().CheckAsync(
            [new RateLimitTarget("Login", "ip:203.0.113.7"), new RateLimitTarget("Login", "account:tom")],
            TestSupport.Ct);

        await store.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(counters => counters.Count == 2 && counters[0].Key != counters[1].Key),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeniedBecomesRateLimitedError()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(RateLimitDecision.Denied(TimeSpan.FromSeconds(90)));

        var result = await CreateService().CheckAsync("Login", "ip:203.0.113.7", TestSupport.Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.RateLimited, result.Error.Kind);
        Assert.Equal(ErrorCodes.RateLimited, result.Error.Code);
        Assert.Equal(TimeSpan.FromSeconds(90), result.Error.RetryAfter);
        Assert.Equal("Too many requests.", result.Error.Detail);
    }

    [Fact]
    public async Task UnknownPolicyIsAProgrammingError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().CheckAsync("NoSuchPolicy", "ip:203.0.113.7", TestSupport.Ct));
    }

    [Fact]
    public async Task DisabledSkipsTheStore()
    {
        options.Enabled = false;

        var result = await CreateService().CheckAsync("Login", "ip:203.0.113.7", TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await store.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task DuringAStoreOutageThisInstanceStillCounts()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TimeoutException("valkey is down"));
        options.Policies["Outage"] = Policy((2, TimeSpan.FromMinutes(1)));
        var service = CreateService();

        var first = await service.CheckAsync("Outage", "ip:203.0.113.7", TestSupport.Ct);
        var second = await service.CheckAsync("Outage", "ip:203.0.113.7", TestSupport.Ct);
        var third = await service.CheckAsync("Outage", "ip:203.0.113.7", TestSupport.Ct);
        var otherIp = await service.CheckAsync("Outage", "ip:203.0.113.8", TestSupport.Ct);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(ErrorKind.RateLimited, third.Error?.Kind);
        Assert.True(third.Error?.RetryAfter > TimeSpan.Zero);
        Assert.True(otherIp.IsSuccess);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateService().CheckAsync("Login", "ip:203.0.113.7", TestSupport.Ct));
    }

    private static RateLimitPolicyOptions Policy(params (int Limit, TimeSpan Window)[] rules) =>
        new() { Rules = [.. rules.Select(r => new RateLimitRuleOptions { PermitLimit = r.Limit, Window = r.Window })] };

    [Fact]
    public async Task TheWaitComesFromTheStoreAndCountsNothing()
    {
        store.WaitAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(TimeSpan.FromSeconds(12));

        var wait = await CreateService().WaitAsync("Login", "account:1", TestSupport.Ct);

        Assert.Equal(TimeSpan.FromSeconds(12), wait);
        await store.DidNotReceiveWithAnyArgs().TryAcquireAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task DuringAStoreOutageAReleaseIsTakenBackHere()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TimeoutException("valkey is down"));
        store.ReleaseAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TimeoutException("valkey is down"));
        options.Policies["Outage"] = Policy((1, TimeSpan.FromMinutes(1)));
        var service = CreateService();

        await service.CheckAsync("Outage", "account:1", TestSupport.Ct);
        await service.ReleaseAsync("Outage", "account:1", TestSupport.Ct);
        var again = await service.CheckAsync("Outage", "account:1", TestSupport.Ct);

        Assert.True(again.IsSuccess);
    }

    private RateLimitService CreateService()
    {
        var monitor = Substitute.For<IOptionsMonitor<RateLimitOptions>>();
        monitor.CurrentValue.Returns(options);
        return new RateLimitService(store, new LocalRateLimiter(TimeProvider.System), monitor, localizer, NullLogger<RateLimitService>.Instance);
    }
}
