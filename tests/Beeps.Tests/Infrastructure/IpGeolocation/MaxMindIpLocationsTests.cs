using System.Globalization;
using Beeps.Features.Accounts;
using Beeps.Infrastructure.IpGeolocation;
using Beeps.Tests.Support;
using MaxMind.Db;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Infrastructure.IpGeolocation;

public sealed class MaxMindIpLocationsTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-Hans");

    private readonly MaxMindIpLocations locations = new(TimeProvider.System, NullLogger<MaxMindIpLocations>.Instance);

    public MaxMindIpLocationsTests() =>
        locations.Use(Open(TestMmdb.Build(
            ("203.0.113.0/24", new Dictionary<string, object>
            {
                ["city"] = TestMmdb.Place(("en", "Shanghai"), ("zh-CN", "上海")),
                ["subdivisions"] = new List<object> { TestMmdb.Place(("en", "Shanghai"), ("zh-CN", "上海")) },
                ["country"] = TestMmdb.Place(("en", "China"), ("zh-CN", "中国")),
            }),
            // As QQWry names Chinese towns: in Chinese only.
            ("198.51.100.0/24", new Dictionary<string, object>
            {
                ["city"] = TestMmdb.Place(("zh-CN", "南京")),
                ["country"] = TestMmdb.Place(("en", "China"), ("zh-CN", "中国")),
            }),
            // Some networks carry the country's code and nothing else.
            ("192.0.2.0/24", new Dictionary<string, object> { ["country"] = new Dictionary<string, object> { ["iso_code"] = "AU" } }))));

    public void Dispose() => locations.Dispose();

    [Fact]
    public void NamesPlacesInThePageLanguage()
    {
        Assert.Equal(new IpLocation("Shanghai", "Shanghai", "China"), locations.Find("203.0.113.7", English));
        Assert.Equal(new IpLocation("上海", "上海", "中国"), locations.Find("203.0.113.7", Chinese));
    }

    [Fact]
    public void FallsBackToAnotherLanguage()
    {
        Assert.Equal(new IpLocation("南京", null, "China"), locations.Find("198.51.100.1", English));
        Assert.Equal(new IpLocation(null, null, "Australia"), locations.Find("192.0.2.1", Chinese));
    }

    [Fact]
    public void ReadsIPv4ClientsOfADualStackSocket()
    {
        Assert.Equal("China", locations.Find("::ffff:203.0.113.7", English)?.Country);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("127.0.0.1")]
    [InlineData("2001:db8::1")]
    public void KnowsNothingOfOtherAddresses(string? address)
    {
        Assert.Null(locations.Find(address, English));
    }

    [Fact]
    public void KnowsNothingBeforeACopyIsLoaded()
    {
        using var empty = new MaxMindIpLocations(TimeProvider.System, NullLogger<MaxMindIpLocations>.Instance);

        Assert.False(empty.IsLoaded);
        Assert.Null(empty.Find("203.0.113.7", English));
    }

    [Fact]
    public void AnswersFromTheNewestCopy()
    {
        locations.Use(Open(TestMmdb.Build(("203.0.113.0/24", new Dictionary<string, object> { ["country"] = TestMmdb.Place(("en", "Japan")) }))));

        Assert.Equal(new IpLocation(null, null, "Japan"), locations.Find("203.0.113.7", English));
    }

    private static Reader Open(byte[] database) => new(new MemoryStream(database));
}
