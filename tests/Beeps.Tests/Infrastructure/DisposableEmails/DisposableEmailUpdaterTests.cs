using System.Net;
using System.Text;
using Beeps.Infrastructure.DisposableEmails;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.DisposableEmails;

public sealed class DisposableEmailUpdaterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"beeps-tests-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly DisposableEmailDomainList list = new();
    private readonly TextHandler upstream = new();

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ADownloadIsUsedAndKeptForTheNextStart()
    {
        upstream.Body = List("mailinator.com");

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.True(list.IsDisposable("tom@mailinator.com"));
        Assert.Equal(new Uri("https://example.test/blocklist.conf"), upstream.Request?.RequestUri);
        Assert.Single(Directory.GetFiles(directory));

        // A restart: the copy on disk answers before anything is fetched, and says when it was fetched.
        var restarted = new DisposableEmailDomainList();
        Assert.Equal(TestData.Now, CreateUpdater(restarted).LoadCached());
        Assert.True(restarted.IsDisposable("tom@mailinator.com"));
    }

    [Fact]
    public async Task ANewerDownloadReplacesTheCopyBefore()
    {
        upstream.Body = List("mailinator.com");
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        time.Now += TimeSpan.FromDays(7);
        upstream.Body = List("yopmail.com");

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.True(list.IsDisposable("tom@yopmail.com"));
        Assert.False(list.IsDisposable("tom@mailinator.com"));
        Assert.Single(Directory.GetFiles(directory));
        Assert.Equal(time.Now, CreateUpdater(new DisposableEmailDomainList()).LoadCached());
    }

    [Fact]
    public async Task UpstreamsCommentsCaseAndStrayLinesAreTidied()
    {
        upstream.Body = $"# a comment\n\nMailinator.COM\r\nnot a domain\n{List()}";

        await CreateUpdater().DownloadAsync(TestSupport.Ct);

        Assert.True(list.IsDisposable("tom@mailinator.com"));
    }

    [Theory]
    [InlineData("<html>Not found</html>")]
    [InlineData("mailinator.com\nyopmail.com\n")]
    public async Task AListThatDoesNotLookLikeOneLeavesTheCopyInUse(string body)
    {
        upstream.Body = List("mailinator.com");
        await CreateUpdater().DownloadAsync(TestSupport.Ct);
        upstream.Body = body;

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.True(list.IsDisposable("tom@mailinator.com"));
        Assert.Equal(List("mailinator.com"), File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
    }

    [Fact]
    public async Task AFailedRequestLeavesNothingBehind()
    {
        upstream.Status = HttpStatusCode.BadGateway;

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateUpdater().DownloadAsync(TestSupport.Ct));

        Assert.False(list.IsLoaded);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ADownloadPastTheLimitIsRefused()
    {
        upstream.Body = List("mailinator.com");

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateUpdater(maxBytes: 64).DownloadAsync(TestSupport.Ct));

        Assert.False(list.IsLoaded);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ACopyThatDoesNotReadAsTheListIsDeleted()
    {
        Directory.CreateDirectory(directory);
        var broken = Path.Combine(directory, "disposable-email-domains.txt");
        File.WriteAllText(broken, "mailinator.com\n");

        Assert.Null(CreateUpdater().LoadCached());
        Assert.False(File.Exists(broken));
        Assert.False(list.IsLoaded);
    }

    private DisposableEmailUpdater CreateUpdater(DisposableEmailDomainList? into = null, long maxBytes = 1024 * 1024)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(DisposableEmailUpdater.HttpClientName).Returns(_ => new HttpClient(upstream, disposeHandler: false));
        var options = Options.Create(new DisposableEmailOptions
        {
            ListUrl = new Uri("https://example.test/blocklist.conf"),
            Directory = directory,
            MaxDownloadBytes = maxBytes,
        });
        return new DisposableEmailUpdater(into ?? list, clients, options, time, NullLogger<DisposableEmailUpdater>.Instance);
    }

    /// <summary>As many domains as a real list needs to be taken for one, with <paramref name="listed"/> among them.</summary>
    private static string List(params string[] listed)
    {
        var text = new StringBuilder();
        foreach (var domain in listed.Concat(Enumerable.Range(0, DisposableEmailUpdater.MinDomains).Select(i => $"throwaway{i}.example")))
        {
            text.Append(domain).Append('\n');
        }

        return text.ToString();
    }

    private sealed class TextHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = "";

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
