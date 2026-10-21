using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.DisposableEmails;

/// <summary>
/// Keeps this instance's copy of the disposable email domains no older than a week. Each instance fetches
/// its own, as with the IP database: the list is reference data anyone can download again, so it needs no
/// coordination, and nothing waits for it. The copy is kept on local disk, so a restart starts from it rather than
/// downloading again. A download is used only once it reads as the list, so a failed or damaged one leaves the copy in
/// use as it was.
/// </summary>
public sealed partial class DisposableEmailUpdater(
    DisposableEmailDomainList list,
    IHttpClientFactory httpClients,
    IOptions<DisposableEmailOptions> options,
    TimeProvider time,
    ILogger<DisposableEmailUpdater> logger) : BackgroundService
{
    public const string HttpClientName = "disposable-email";

    /// <summary>
    /// Upstream lists thousands: fewer is an error page or a broken file, not a cleanup, and using it would let through
    /// everything the copy before refused.
    /// </summary>
    internal const int MinDomains = 1000;

    private const string FileName = "disposable-email-domains.txt";

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
                // GitHub away, a bad file, a full disk: the copy loaded (if any) keeps answering until the next try.
                LogDownloadFailed(logger, exception, settings.RetryInterval);
                next = time.GetUtcNow() + settings.RetryInterval;
            }
        }
    }

    /// <summary>Loads the copy on disk, if it reads as the list; returns when it was fetched.</summary>
    internal DateTimeOffset? LoadCached()
    {
        var path = Path.Combine(options.Value.ResolvedDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var domains = Parse(File.ReadAllText(path));
            var fetchedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            list.Use(domains);
            LogLoaded(logger, domains.Count, path);
            return fetchedAt;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LogCachedUnusable(logger, exception, path);
            TryDelete(path);
            return null;
        }
    }

    /// <summary>Fetches the latest list, keeps it for the next start, and starts using it.</summary>
    internal async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.GetAsync(settings.ListUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > settings.MaxDownloadBytes)
        {
            throw new InvalidDataException($"The disposable email list is larger than {settings.MaxDownloadBytes} bytes.");
        }

        string text;
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            text = await ReadAsync(body, settings.MaxDownloadBytes, cancellationToken);
        }

        var domains = Parse(text);
        var directory = settings.ResolvedDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        // Unique even for two instances sharing the folder (several on one machine); the move replaces the copy whole.
        var partial = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.part");
        try
        {
            await File.WriteAllTextAsync(partial, text, cancellationToken);
            File.SetLastWriteTimeUtc(partial, time.GetUtcNow().UtcDateTime);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        list.Use(domains);
        LogDownloaded(logger, domains.Count, path);
    }

    /// <summary>One domain a line, as upstream writes them; comments and blank lines are skipped, and so is anything else.</summary>
    internal static HashSet<string> Parse(string text)
    {
        var domains = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var domain = line.Trim().ToLowerInvariant();
            if (DomainPattern().IsMatch(domain))
            {
                domains.Add(domain);
            }
        }

        return domains.Count >= MinDomains
            ? domains
            : throw new InvalidDataException($"The disposable email list has {domains.Count} domains, fewer than {MinDomains}.");
    }

    private static async Task<string> ReadAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException($"The disposable email list is larger than {maxBytes} bytes.");
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

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)+\z")]
    private static partial Regex DomainPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Disposable email domains are disabled on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} disposable email domains from {Path}")]
    private static partial void LogLoaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {Count} disposable email domains to {Path}")]
    private static partial void LogDownloaded(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The disposable email list at {Path} cannot be read; it is deleted")]
    private static partial void LogCachedUnusable(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading the disposable email list failed; trying again in {RetryInterval}")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception, TimeSpan retryInterval);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not delete {Path}")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
