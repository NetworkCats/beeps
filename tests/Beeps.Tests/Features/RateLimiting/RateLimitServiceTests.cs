using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Beeps;
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
    public async Task StoreOutageFailsOpen()
    {
        store.TryAcquireAsync(default!, TestSupport.Ct).ThrowsAsyncForAnyArgs(new TimeoutException("valkey is down"));

        var result = await CreateService().CheckAsync("Login", "ip:203.0.113.7", TestSupport.Ct);

        Assert.True(result.IsSuccess);
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

    private RateLimitService CreateService()
    {
        var monitor = Substitute.For<IOptionsMonitor<RateLimitOptions>>();
        monitor.CurrentValue.Returns(options);
        return new RateLimitService(store, monitor, localizer, NullLogger<RateLimitService>.Instance);
    }
}
