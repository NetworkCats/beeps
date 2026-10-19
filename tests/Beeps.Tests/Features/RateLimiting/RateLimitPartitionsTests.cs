using Beeps.Features.RateLimiting;

namespace Beeps.Tests.Features.RateLimiting;

public sealed class RateLimitPartitionsTests
{
    [Theory]
    [InlineData("203.0.113.7", "ip:203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "ip:203.0.113.7")]
    // One host is usually given a whole /64: every address in it shares a counter.
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "ip:2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "ip:2001:db8:1:2::/64")]
    [InlineData(null, "ip:")]
    public void AddressesAreCountedByHost(string? clientIp, string partition) =>
        Assert.Equal(partition, RateLimitPartitions.Ip(clientIp));
}
