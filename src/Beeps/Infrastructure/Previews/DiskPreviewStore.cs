using Beeps.Features.Previews;
using Microsoft.Extensions.Options;

namespace Beeps.Infrastructure.Previews;

/// <summary>
/// <see cref="IPreviewStore"/> on local disk (<see cref="PreviewOptions.Directory"/>, a volume of its own for each
/// instance), as <c>{kind}/{first two of the id}/{id}.{culture}.{version}.png</c>: a folder holds a few hundred
/// previews, not all of them. An image is written beside its place and moved into it, so a reader never gets one half
/// written; two instances or requests drawing the same one at once only write the same bytes twice. A failing disk
/// costs the drawing again, never the image: each is still drawn and served.
/// </summary>
public sealed partial class DiskPreviewStore(IOptions<PreviewOptions> options, TimeProvider time, ILogger<DiskPreviewStore> logger) : IPreviewStore
{
    /// <summary>An image read again is marked as used at most this often (<see cref="PreviewOptions.Retention"/>).</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromDays(1);

    public Stream? Open(PreviewName name)
    {
        var path = PathOf(name);
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 1, useAsync: true);
            var now = time.GetUtcNow().UtcDateTime;
            if (File.GetLastWriteTimeUtc(path) < now - TouchInterval)
            {
                File.SetLastWriteTimeUtc(path, now);
            }

            return stream;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(logger, path, e);
            return null;
        }
    }

    public async Task SaveAsync(PreviewName name, byte[] png, CancellationToken cancellationToken)
    {
        var path = PathOf(name);
        var written = $"{path}.{Guid.NewGuid():N}{PreviewCleanup.PartialExtension}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(written, png, cancellationToken);
            File.Move(written, path, overwrite: true);
            Delete(Path.GetDirectoryName(path)!, $"{name.Id}.{name.Culture}.*.png", except: path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(logger, path, e);
            // Left behind, the sweep takes it (PreviewCleanup).
            _ = TryDelete(written);
        }
    }

    public void Remove(string kind, string id)
    {
        var folder = Path.Combine(options.Value.ResolvedDirectory, kind, Shard(id));
        try
        {
            Delete(folder, $"{id}.*.png", except: null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(logger, folder, e);
        }
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Delete(string folder, string pattern, string? except)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, pattern).Where(f => f != except))
        {
            File.Delete(file);
        }
    }

    // The parts are the app's own (ids, supported cultures, hex hashes): none can lead outside the folder.
    private string PathOf(PreviewName name) =>
        Path.Combine(options.Value.ResolvedDirectory, name.Kind, Shard(name.Id), $"{name.Id}.{name.Culture}.{name.Version}.png");

    private static string Shard(string id) => id[..Math.Min(2, id.Length)];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the kept preview {Path}; drawing it again")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write or remove the preview {Path}; it is drawn again when asked for")]
    private static partial void LogWriteFailed(ILogger logger, string path, Exception exception);
}
