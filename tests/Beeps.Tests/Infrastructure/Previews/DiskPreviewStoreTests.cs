using Beeps.Features.Previews;
using Beeps.Infrastructure.Previews;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Previews;

public sealed class DiskPreviewStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"beeps-previews-{Guid.NewGuid():N}");
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly DiskPreviewStore store;
    private readonly PreviewCleanup cleanup;

    public DiskPreviewStoreTests()
    {
        var options = Options.Create(new PreviewOptions { Directory = directory });
        store = new DiskPreviewStore(options, time, NullLogger<DiskPreviewStore>.Instance);
        cleanup = new PreviewCleanup(options, time, NullLogger<PreviewCleanup>.Instance);
    }

    public void Dispose()
    {
        cleanup.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AKeptImageIsReadBackByItsVersion()
    {
        var name = Name("en", "aaaa");

        await store.SaveAsync(name, [1, 2, 3], TestSupport.Ct);

        Assert.Equal([1, 2, 3], await ReadAsync(name));
        Assert.Null(store.Open(name with { Version = "bbbb" }));
    }

    [Fact]
    public async Task ANewVersionReplacesTheOldInItsLanguageOnly()
    {
        await store.SaveAsync(Name("en", "aaaa"), [1], TestSupport.Ct);
        await store.SaveAsync(Name("zh-Hans", "aaaa"), [2], TestSupport.Ct);

        await store.SaveAsync(Name("en", "bbbb"), [3], TestSupport.Ct);

        Assert.Null(store.Open(Name("en", "aaaa")));
        Assert.Equal([3], await ReadAsync(Name("en", "bbbb")));
        Assert.Equal([2], await ReadAsync(Name("zh-Hans", "aaaa")));
    }

    [Fact]
    public async Task RemovingAPreviewRemovesEveryLanguageOfItAndNothingElse()
    {
        var other = new PreviewName(PreviewEndpoints.Beeps, "0199f1a2-0000-7000-8000-00000000ffff", "en", "aaaa");
        await store.SaveAsync(Name("en", "aaaa"), [1], TestSupport.Ct);
        await store.SaveAsync(Name("zh-Hans", "aaaa"), [2], TestSupport.Ct);
        await store.SaveAsync(other, [3], TestSupport.Ct);

        store.Remove(PreviewEndpoints.Beeps, Name("en", "aaaa").Id);

        Assert.Null(store.Open(Name("en", "aaaa")));
        Assert.Null(store.Open(Name("zh-Hans", "aaaa")));
        Assert.Equal([3], await ReadAsync(other));
    }

    [Fact]
    public async Task TheSweepDeletesWhatNoOneHasAskedForInTheRetentionAndCutShortWrites()
    {
        await store.SaveAsync(Name("en", "aaaa"), [1], TestSupport.Ct);
        await store.SaveAsync(Name("zh-Hans", "aaaa"), [2], TestSupport.Ct);
        var partial = Path.Combine(directory, "beeps", "01", "x.png.0123.partial");
        await File.WriteAllBytesAsync(partial, [9], TestSupport.Ct);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, TestData.Now.UtcDateTime);
        }

        // Read 20 days on, the English one is marked as used then; the Chinese one is not.
        time.Now += TimeSpan.FromDays(20);
        Assert.NotNull(await ReadAsync(Name("en", "aaaa")));
        time.Now += TimeSpan.FromDays(15);

        Assert.Equal(2, cleanup.Sweep());
        Assert.NotNull(await ReadAsync(Name("en", "aaaa")));
        Assert.Null(store.Open(Name("zh-Hans", "aaaa")));
        Assert.False(File.Exists(partial));
    }

    private static PreviewName Name(string culture, string version) =>
        new(PreviewEndpoints.Beeps, "0199f1a2-0000-7000-8000-000000000001", culture, version);

    private async Task<byte[]?> ReadAsync(PreviewName name)
    {
        await using var stream = store.Open(name);
        if (stream is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestSupport.Ct);
        return copy.ToArray();
    }
}
