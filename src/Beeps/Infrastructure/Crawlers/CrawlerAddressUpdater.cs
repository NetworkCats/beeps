using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Crawlers;

/// <summary>
/// Keeps this instance's copy of Google's and Bing's crawler addresses no older than a day. Each instance fetches its
/// own, as with the disposable email domains: the lists are published for anyone to download, so they need no
/// coordination. The copy is kept on local disk, so a restart starts from it rather than downloading again. A download
/// is used only once every list reads as one, so a failed or damaged one leaves the copy in use as it was.
/// </summary>
public sealed partial class CrawlerAddressUpdater(
    CrawlerAddressList list,
    IHttpClientFactory httpClients,
    IOptions<CrawlerAddressOptions> options,
    TimeProvider time,
    ILogger<CrawlerAddressUpdater> logger) : BackgroundService
{
    public const string HttpClientName = "crawler-addresses";

    /// <summary>
    /// Googlebot and Google's other crawlers; the fetchers a person sets off (Search Console's sitemap test among
    /// them), Google's own and others'; and Bingbot.
    /// </summary>
    internal static readonly IReadOnlyList<Uri> Sources =
    [
        new("https://developers.google.com/static/crawling/ipranges/common-crawlers.json"),
        new("https://developers.google.com/static/crawling/ipranges/user-triggered-fetchers.json"),
        new("https://developers.google.com/static/crawling/ipranges/user-triggered-fetchers-google.json"),
        new("https://www.bing.com/toolbox/bingbot.json"),
    ];

    private const string FileName = "crawler-networks.txt";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var next = LoadCached() is { } fetchedAt ? fetchedAt + settings.RefreshInterval : time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var wait = next - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, stoppingToken);
                }

                await DownloadAsync(stoppingToken);
                next = time.GetUtcNow() + settings.RefreshInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Google or Bing away, a bad file, a full disk: the copy loaded (if any) keeps answering until the next try.
                LogDownloadFailed(logger, exception, settings.RetryInterval);
                next = time.GetUtcNow() + settings.RetryInterval;
            }
        }
    }

    /// <summary>Loads the copy on disk, if it reads as one; returns when it was fetched.</summary>
    internal DateTimeOffset? LoadCached()
    {
        var path = Path.Combine(options.Value.ResolvedDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var networks = ParseCopy(File.ReadAllText(path));
            var fetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            list.Use(networks);
            LogLoaded(logger, networks.Count, path);
            return fetchedAt;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LogCachedUnusable(logger, exception, path);
            TryDelete(path);
            return null;
        }
    }

    /// <summary>Fetches every list, keeps them for the next start as one, and starts using them.</summary>
    internal async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var networks = new HashSet<IPNetwork>();
        using (var http = httpClients.CreateClient(HttpClientName))
        {
            foreach (var source in Sources)
            {
                networks.UnionWith(Parse(await FetchAsync(http, source, settings.MaxDownloadBytes, cancellationToken), source));
            }
        }

        var text = new StringBuilder();
        foreach (var network in networks)
        {
            text.Append(network).Append('\n');
        }

        var directory = settings.ResolvedDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        // Unique even for two instances sharing the folder (several on one machine); the move replaces the copy whole.
        var partial = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.part");
        try
        {
            await File.WriteAllTextAsync(partial, text.ToString(), cancellationToken);
            File.SetLastWriteTimeUtc(partial, time.GetUtcNow().UtcDateTime);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        list.Use([.. networks]);
        LogDownloaded(logger, networks.Count, path);
    }

    /// <summary>
    /// One list as Google and Bing both write it: <c>{"prefixes": [{"ipv4Prefix": "66.249.64.0/27"}, {"ipv6Prefix": …}]}</c>.
    /// Entries of another kind are skipped; a list with no range at all is an error page or a broken file.
    /// </summary>
    internal static List<IPNetwork> Parse(string json, Uri source)
    {
        var networks = new List<IPNetwork>();
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var prefix in document.RootElement.GetProperty("prefixes").EnumerateArray())
            {
                if (prefix.ValueKind == JsonValueKind.Object
                    && (prefix.TryGetProperty("ipv4Prefix", out var value) || prefix.TryGetProperty("ipv6Prefix", out value))
                    && value.ValueKind == JsonValueKind.String
                    && IPNetwork.TryParse(value.GetString(), out var network))
                {
                    networks.Add(network);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"{source} is not a list of address ranges.", exception);
        }

        return networks.Count > 0 ? networks : throw new InvalidDataException($"{source} lists no address ranges.");
    }

    /// <summary>The copy on disk: one range a line, as <see cref="DownloadAsync"/> writes it.</summary>
    internal static List<IPNetwork> ParseCopy(string text)
    {
        var networks = new List<IPNetwork>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            networks.Add(IPNetwork.TryParse(line, out var network) ? network : throw new InvalidDataException($"'{line}' is not an address range."));
        }

        return networks.Count > 0 ? networks : throw new InvalidDataException("The copy lists no address ranges.");
    }

    private static async Task<string> FetchAsync(HttpClient http, Uri source, long maxBytes, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException($"{source} is larger than {maxBytes} bytes.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException($"{source} is larger than {maxBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(logger, exception, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Crawler addresses are disabled on this instance; the sitemap is served to no one")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} crawler address ranges from {Path}")]
    private static partial void LogLoaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {Count} crawler address ranges to {Path}")]
    private static partial void LogDownloaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The crawler addresses at {Path} cannot be read; they are deleted")]
    private static partial void LogCachedUnusable(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading the crawler addresses failed; trying again in {RetryInterval}")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception, TimeSpan retryInterval);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not delete {Path}")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
