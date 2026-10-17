using System.Text;
using Beeps.Features.Accounts;
using Beeps.Infrastructure.Caching;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Caching;

public sealed class ContentCacheTests : IDisposable
{
    private static readonly Guid A = Guid.CreateVersion7(TestData.Now);
    private static readonly Guid B = Guid.CreateVersion7(TestData.Now.AddSeconds(1));
    private static readonly Guid C = Guid.CreateVersion7(TestData.Now.AddSeconds(2));

    private readonly InMemorySharedContentCache shared = new();
    private readonly CacheOptions options = new();
    private readonly FixedTimeProvider time = new(TestData.Now);
    private readonly List<IReadOnlyList<Guid>> loads = [];
    private readonly ContentCache cache;

    public ContentCacheTests() => cache = new ContentCache(shared, Options.Create(options), time, NullLogger<ContentCache>.Instance);

    public void Dispose() => cache.Dispose();

    [Fact]
    public async Task LoadsWhatNeitherLevelHasInOneCallAndCachesItInBoth()
    {
        var found = await GetAsync(A, B, C);

        Assert.Equal([A, B, C], found.Keys.Order());
        Assert.Equal([A, B, C], Assert.Single(loads).Order());
        Assert.Single(shared.Gets);
        Assert.Equal(3, shared.Entries.Count);
        Assert.All(shared.Expirations.Values, e => Assert.Equal(options.ActorExpiration, e));
    }

    [Fact]
    public async Task ServesItsOwnCopyWithoutAskingValkeyOrTheDatabase()
    {
        await GetAsync(A);

        var found = await GetAsync(A);

        Assert.Equal("actor " + A, found[A].DisplayName);
        Assert.Single(loads);
        Assert.Single(shared.Gets);
    }

    [Fact]
    public async Task ServesValkeyEntriesWithoutLoadingThem()
    {
        await GetAsync(A);
        cache.RemoveLocal([CacheKinds.Actor.Key(A)]);

        await GetAsync(A, B);

        Assert.Equal(2, loads.Count);
        Assert.Equal([B], loads[1]);
        Assert.Equal(2, shared.Gets.Count);
    }

    [Fact]
    public async Task IdsTheLoaderDoesNotFindAreLeftOutAndNotCached()
    {
        var found = await GetAsync(A, B, missing: [B]);

        Assert.Equal([A], found.Keys);
        Assert.False(shared.Entries.ContainsKey(CacheKinds.Actor.Key(B)));
        await GetAsync(B, missing: [B]);
        Assert.Equal(2, loads.Count);
    }

    [Fact]
    public async Task AValkeyOutageFallsBackToTheDatabaseAndLeavesValkeyAloneForAWhile()
    {
        shared.Failure = new InvalidOperationException("Valkey is down");

        var first = await GetAsync(A);
        cache.ClearLocal();
        var second = await GetAsync(A);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(2, loads.Count);
        // The second read did not even try Valkey.
        Assert.Empty(shared.Gets);

        shared.Failure = null;
        time.Now += options.SharedPauseAfterError;
        cache.ClearLocal();
        await GetAsync(A);
        Assert.Single(shared.Gets);
    }

    [Fact]
    public async Task AnEntryThatDoesNotReadIsLoadedAgainAndOverwritten()
    {
        shared.Entries[CacheKinds.Actor.Key(A)] = Encoding.UTF8.GetBytes("{not json");

        var found = await GetAsync(A);

        Assert.Single(found);
        Assert.Single(loads);
        Assert.StartsWith("{\"id\"", Encoding.UTF8.GetString(shared.Entries[CacheKinds.Actor.Key(A)]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OffItAlwaysLoads()
    {
        options.Enabled = false;

        await GetAsync(A);
        await GetAsync(A);

        Assert.Equal(2, loads.Count);
        Assert.Empty(shared.Gets);
        Assert.Empty(shared.Entries);
    }

    private Task<IReadOnlyDictionary<Guid, CachedActor>> GetAsync(params Guid[] ids) => GetAsync(ids, []);

    private Task<IReadOnlyDictionary<Guid, CachedActor>> GetAsync(Guid id, Guid[] missing) => GetAsync([id], missing);

    private Task<IReadOnlyDictionary<Guid, CachedActor>> GetAsync(Guid a, Guid b, Guid[] missing) => GetAsync([a, b], missing);

    private Task<IReadOnlyDictionary<Guid, CachedActor>> GetAsync(Guid[] ids, Guid[] missing) =>
        cache.GetManyAsync(
            CacheKinds.Actor,
            ids,
            (wanted, _) =>
            {
                loads.Add(wanted);
                IReadOnlyDictionary<Guid, CachedActor> found = wanted.Where(id => !missing.Contains(id)).ToDictionary(id => id, Actor);
                return Task.FromResult(found);
            },
            TestSupport.Ct);

    private static CachedActor Actor(Guid id) =>
        new(id, ActorKind.Human, "h" + id.ToString("N")[..8], "actor " + id, null, null, null, null, TestData.Now, Deleted: false, Banned: false);
}
