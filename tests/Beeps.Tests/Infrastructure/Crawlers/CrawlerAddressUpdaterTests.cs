using System.Net;
using Beeps.Infrastructure.Crawlers;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Crawlers;

public sealed class CrawlerAddressUpdaterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"beeps-tests-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly CrawlerAddressList list = new();
    private readonly ListsHandler upstream = new();

    public CrawlerAddressUpdaterTests()
    {
        foreach (var source in CrawlerAddressUpdater.Sources)
        {
            upstream.Bodies[source] = """{"creationTime": "2026-10-20T14:46:18.000000", "prefixes": [{"ipv4Prefix": "192.0.2.0/24"}]}""";
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EveryListIsFetchedUsedAndKeptForTheNextStart()
    {
        upstream.Bodies[CrawlerAddressUpdater.Sources[0]] = """{"prefixes": [{"ipv6Prefix": "2001:db8:1::/64"}, {"ipv4Prefix": "198.51.100.0/27"}]}""";
        upstream.Bodies[CrawlerAddressUpdater.Sources[^1]] = """{"prefixes": [{"ipv4Prefix": "203.0.113.0/24"}]}""";

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.Equal(CrawlerAddressUpdater.Sources, upstream.Requested);
        Assert.True(list.Contains(IPAddress.Parse("2001:db8:1::7")));
        Assert.True(list.Contains(IPAddress.Parse("198.51.100.31")));
        Assert.False(list.Contains(IPAddress.Parse("198.51.100.32")));
        Assert.True(list.Contains(IPAddress.Parse("203.0.113.9")));
        // How Kestrel on [::] sees an IPv4 visitor.
        Assert.True(list.Contains(IPAddress.Parse("::ffff:192.0.2.1")));
        Assert.Single(Directory.GetFiles(directory));

        // A restart: the copy on disk answers before anything is fetched, and says when it was fetched.
        var restarted = new CrawlerAddressList();
        Assert.Equal(TestData.Now, CreateUpdater(restarted).LoadCached());
        Assert.True(restarted.Contains(IPAddress.Parse("2001:db8:1::7")));
        Assert.True(restarted.Contains(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public async Task ANewerDownloadReplacesTheCopyBefore()
    {
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        time.Now += TimeSpan.FromDays(1);
        upstream.Bodies[CrawlerAddressUpdater.Sources[^1]] = """{"prefixes": [{"ipv4Prefix": "203.0.113.0/24"}]}""";
        foreach (var source in CrawlerAddressUpdater.Sources.SkipLast(1))
        {
            upstream.Bodies[source] = """{"prefixes": [{"ipv4Prefix": "198.51.100.0/24"}]}""";
        }

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.False(list.Contains(IPAddress.Parse("192.0.2.1")));
        Assert.True(list.Contains(IPAddress.Parse("203.0.113.1")));
        Assert.Single(Directory.GetFiles(directory));
        Assert.Equal(time.Now, CreateUpdater(new CrawlerAddressList()).LoadCached());
    }

    [Fact]
    public async Task EntriesOfAnotherKindAreSkipped()
    {
        upstream.Bodies[CrawlerAddressUpdater.Sources[0]] =
            """{"prefixes": [{"note": "x"}, {"ipv4Prefix": "not a range"}, "198.51.100.0/24", {"ipv4Prefix": "203.0.113.0/24"}]}""";

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.True(list.Contains(IPAddress.Parse("203.0.113.1")));
        Assert.False(list.Contains(IPAddress.Parse("198.51.100.1")));
    }

    [Theory]
    [InlineData("<html>Not found</html>")]
    [InlineData("""{"creationTime": "2026-10-20T14:46:18.000000"}""")]
    [InlineData("""{"prefixes": []}""")]
    [InlineData("""{"prefixes": {}}""")]
    public async Task OneListThatDoesNotLookLikeOneLeavesTheCopyInUse(string body)
    {
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        var copy = File.ReadAllText(Assert.Single(Directory.GetFiles(directory)));
        upstream.Bodies[CrawlerAddressUpdater.Sources[^1]] = body;

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.True(list.Contains(IPAddress.Parse("192.0.2.1")));
        Assert.Equal(copy, File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
    }

    [Fact]
    public async Task AFailedRequestLeavesNothingBehind()
    {
        upstream.Failing = CrawlerAddressUpdater.Sources[^1];

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.False(list.IsLoaded);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ADownloadPastTheLimitIsRefused()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateUpdater(maxBytes: 32).DownloadAsync(TestSupport.Ct));

        Assert.False(list.IsLoaded);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ACopyThatDoesNotReadAsOneIsDeleted()
    {
        Directory.CreateDirectory(directory);
        var broken = Path.Combine(directory, "crawler-networks.txt");
        File.WriteAllText(broken, "192.0.2.0/24\n<html>\n");

        Assert.Null(CreateUpdater().LoadCached());
        Assert.False(File.Exists(broken));
        Assert.False(list.IsLoaded);
    }

    private CrawlerAddressUpdater CreateUpdater(CrawlerAddressList? into = null, long maxBytes = 1024 * 1024)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(CrawlerAddressUpdater.HttpClientName).Returns(_ => new HttpClient(upstream, disposeHandler: false));
        var options = Options.Create(new CrawlerAddressOptions { Directory = directory, MaxDownloadBytes = maxBytes });
        return new CrawlerAddressUpdater(into ?? list, clients, options, time, NullLogger<CrawlerAddressUpdater>.Instance);
    }

    private sealed class ListsHandler : HttpMessageHandler
    {
        public Dictionary<Uri, string> Bodies { get; } = [];

        public Uri? Failing { get; set; }

        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            Requested.Add(url);
            return Task.FromResult(url == Failing
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Bodies[url]) });
        }
    }
}
