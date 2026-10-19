using System.Globalization;
using System.Net;
using Beeps.Infrastructure.IpGeolocation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.IpGeolocation;

public sealed class IpLocationUpdaterTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private readonly string directory = Path.Combine(Path.GetTempPath(), $"beeps-tests-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly MaxMindIpLocations locations;
    private readonly BytesHandler upstream = new();

    public IpLocationUpdaterTests() => locations = new(time, NullLogger<MaxMindIpLocations>.Instance);

    public void Dispose()
    {
        locations.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ADownloadIsUsedAndKeptForTheNextStart()
    {
        upstream.Body = Database("Japan");

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.Equal("Japan", locations.Find("203.0.113.7", English)?.Country);
        Assert.Equal(new Uri("https://example.test/Merged-IP.mmdb"), upstream.Request?.RequestUri);
        Assert.Single(Directory.GetFiles(directory, "*.mmdb"));

        // A restart: the copy on disk answers before anything is fetched, and says when it was fetched.
        using var restarted = new MaxMindIpLocations(time, NullLogger<MaxMindIpLocations>.Instance);
        Assert.Equal(TestData.Now, CreateUpdater(restarted).LoadCached());
        Assert.Equal("Japan", restarted.Find("203.0.113.7", English)?.Country);
    }

    [Fact]
    public async Task ANewerDownloadReplacesTheCopyBefore()
    {
        upstream.Body = Database("Japan");
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        time.Now += TimeSpan.FromDays(1);
        upstream.Body = Database("France");

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.Equal("France", locations.Find("203.0.113.7", English)?.Country);
        Assert.Single(Directory.GetFiles(directory, "*.mmdb"));
    }

    [Fact]
    public async Task ADamagedDownloadLeavesTheCopyInUse()
    {
        upstream.Body = Database("Japan");
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        upstream.Body = "<html>Not found</html>"u8.ToArray();

        await Assert.ThrowsAnyAsync<Exception>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.Equal("Japan", locations.Find("203.0.113.7", English)?.Country);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task AFailedRequestLeavesNothingBehind()
    {
        upstream.Status = HttpStatusCode.BadGateway;

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.False(locations.IsLoaded);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task ADownloadPastTheLimitIsRefused()
    {
        upstream.Body = Database("Japan");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateUpdater(maxBytes: 64).DownloadAsync(TestSupport.Ct));

        Assert.False(locations.IsLoaded);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void ACopyThatDoesNotOpenIsDeleted()
    {
        Directory.CreateDirectory(directory);
        var broken = Path.Combine(directory, "Merged-IP-20261016T083000Z-0.mmdb");
        File.WriteAllBytes(broken, [1, 2, 3]);

        Assert.Null(CreateUpdater().LoadCached());
        Assert.False(File.Exists(broken));
        Assert.False(locations.IsLoaded);
    }

    private IpLocationUpdater CreateUpdater(MaxMindIpLocations? into = null, long maxBytes = 1024 * 1024)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(IpLocationUpdater.HttpClientName).Returns(_ => new HttpClient(upstream, disposeHandler: false));
        var options = Options.Create(new IpLocationOptions
        {
            DatabaseUrl = new Uri("https://example.test/Merged-IP.mmdb"),
            Directory = directory,
            MaxDownloadBytes = maxBytes,
        });
        return new IpLocationUpdater(into ?? locations, clients, options, time, NullLogger<IpLocationUpdater>.Instance);
    }

    private static byte[] Database(string country) =>
        TestMmdb.Build(("203.0.113.0/24", new Dictionary<string, object> { ["country"] = TestMmdb.Place(("en", country)) }));

    private sealed class BytesHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public byte[] Body { get; set; } = [];

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }
}
