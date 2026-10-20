using System.Globalization;
using MaxMind.Db;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.IpGeolocation;

/// <summary>
/// Keeps this instance's copy of the IP database (PLAN 6.14) no older than a day. Each instance fetches its own: the
/// file is reference data anyone can download again, not state, so it needs no coordination, and nothing waits for it.
/// The copy is kept on local disk, so a restart starts from it rather than downloading again. A download is used only
/// once it opens as a database, so a failed or damaged one leaves the copy in use as it was.
/// </summary>
public sealed partial class IpLocationUpdater(
    MaxMindIpLocations locations,
    IHttpClientFactory httpClients,
    IOptions<IpLocationOptions> options,
    TimeProvider time,
    ILogger<IpLocationUpdater> logger) : BackgroundService
{
    public const string HttpClientName = "ip-location";

    private const string FilePrefix = "Merged-IP-";
    private const string FileExtension = ".mmdb";
    private const string StampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>No download takes this long: a part file this old was left by an instance that stopped mid-download.</summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromDays(1);

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
                // Upstream away, a bad file, a full disk: the copy loaded (if any) keeps answering until the next try.
                LogDownloadFailed(logger, exception, settings.RetryInterval);
                next = time.GetUtcNow() + settings.RetryInterval;
            }
        }
    }

    /// <summary>Loads the newest copy on disk, if one opens; returns when it was fetched.</summary>
    internal DateTimeOffset? LoadCached()
    {
        var directory = options.Value.ResolvedDirectory;
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var (path, fetchedAt) in Copies(directory).OrderByDescending(c => c.FetchedAt))
        {
            try
            {
                Load(path);
                LogLoaded(logger, path);
                return fetchedAt;
            }
            catch (Exception exception)
            {
                // Not only InvalidDatabaseException: an empty file left by a crash throws ArgumentException, and an
                // exception out of here would stop the host on every start.
                LogCachedUnusable(logger, exception, path);
                TryDelete(path);
            }
        }

        return null;
    }

    /// <summary>Fetches the latest database and starts using it, then drops the copies before it.</summary>
    internal async Task DownloadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var directory = settings.ResolvedDirectory;
        Directory.CreateDirectory(directory);
        var stamp = time.GetUtcNow().UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
        // Unique even for two instances sharing the folder (several on one machine).
        var partial = Path.Combine(directory, $"{FilePrefix}{stamp}-{Guid.NewGuid():N}.part");
        var path = Path.Combine(directory, $"{FilePrefix}{stamp}-{Guid.NewGuid():N}{FileExtension}");
        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            using var response = await http.GetAsync(settings.DatabaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > settings.MaxDownloadBytes)
            {
                throw new InvalidDataException($"The IP database is larger than {settings.MaxDownloadBytes} bytes.");
            }

            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await CopyAsync(body, file, settings.MaxDownloadBytes, cancellationToken);
            }

            File.Move(partial, path);
            Load(path);
        }
        catch
        {
            TryDelete(partial);
            TryDelete(path);
            throw;
        }

        LogDownloaded(logger, path);
        // Other instances may share the folder: only copies older than this one go, and only downloads long abandoned.
        var fetchedAt = FetchedAt(path);
        foreach (var file in Directory.EnumerateFiles(directory, $"{FilePrefix}*"))
        {
            var stale = FetchedAt(file) is { } at && (file.EndsWith(FileExtension, StringComparison.Ordinal)
                ? at < fetchedAt
                : file.EndsWith(".part", StringComparison.Ordinal) && at < fetchedAt - AbandonedAfter);
            if (stale)
            {
                // The copy just replaced is still mapped until it retires; on POSIX it can go now, elsewhere next time.
                TryDelete(file);
            }
        }
    }

    private void Load(string path)
    {
        var reader = new Reader(path, FileAccessMode.MemoryMapped);
        locations.Use(reader);
    }

    private static IEnumerable<(string Path, DateTimeOffset FetchedAt)> Copies(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}"))
        {
            if (FetchedAt(path) is { } fetchedAt)
            {
                yield return (path, fetchedAt);
            }
        }
    }

    /// <summary>When the file was fetched (or started to be), from the stamp in its name.</summary>
    private static DateTimeOffset? FetchedAt(string path)
    {
        var name = Path.GetFileName(path);
        var stamp = name.Length > FilePrefix.Length + 16 ? name.Substring(FilePrefix.Length, 16) : "";
        return DateTimeOffset.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fetchedAt) ? fetchedAt : null;
    }

    private static async Task CopyAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException($"The IP database is larger than {maxBytes} bytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "IP locations are disabled on this instance")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded the IP database from {Path}")]
    private static partial void LogLoaded(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded the IP database to {Path}")]
    private static partial void LogDownloaded(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The IP database at {Path} cannot be opened; it is deleted")]
    private static partial void LogCachedUnusable(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Downloading the IP database failed; trying again in {RetryInterval}")]
    private static partial void LogDownloadFailed(ILogger logger, Exception exception, TimeSpan retryInterval);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not delete {Path}")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
