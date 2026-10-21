using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Beeps.Features.Accounts;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Infrastructure.Caching;
using Beeps.Infrastructure.Data;
using Beeps.Infrastructure.Data.Stores;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Caching;

/// <summary>
/// The worker against an in-memory Valkey. Page tags are worked out from cached entries only: the database
/// context behind <see cref="ContentReads"/> points nowhere, so a read that reached it would fail the test.
/// </summary>
public sealed class CacheInvalidatorTests : IAsyncDisposable
{
    private static readonly Guid Owner = Guid.CreateVersion7(TestData.Now);
    private static readonly Guid Agent = Guid.CreateVersion7(TestData.Now.AddSeconds(1));
    private static readonly Guid Root = Guid.CreateVersion7(TestData.Now.AddSeconds(2));
    private static readonly Guid Reply = Guid.CreateVersion7(TestData.Now.AddSeconds(3));

    private readonly InMemorySharedContentCache shared = new();
    private readonly CacheOptions options = new() { SecondPassDelay = TimeSpan.FromMilliseconds(50) };
    private readonly IOutputCacheStore pages = Substitute.For<IOutputCacheStore>();
    private readonly HybridCache hybrid = Substitute.For<HybridCache>();
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly ContentCache cache;
    private readonly ServiceProvider services;
    private readonly CacheInvalidator invalidator;
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);

    public CacheInvalidatorTests()
    {
        cache = new ContentCache(shared, Options.Create(options), TimeProvider.System, NullLogger<ContentCache>.Instance);
        services = new ServiceCollection()
            .AddDbContext<BeepsDbContext>(o => o.UseNpgsql("Host=postgres.invalid;Database=beeps").UseSnakeCaseNamingConvention())
            .AddSingleton(cache)
            .AddSingleton(outbox)
            .AddScoped<ContentReads>()
            .BuildServiceProvider();
        invalidator = new CacheInvalidator(
            cache, shared, services.GetRequiredService<IServiceScopeFactory>(), pages, hybrid, Options.Create(options), TimeProvider.System, NullLogger<CacheInvalidator>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await invalidator.StopAsync(CancellationToken.None);
        invalidator.Dispose();
        await services.DisposeAsync();
        cache.Dispose();
        stop.Dispose();
    }

    [Fact]
    public async Task DropsThisInstancesCopyBeforeReturning()
    {
        shared.Put(CacheKinds.Actor, Owner, Human(Owner));
        await ReadActorAsync(Owner);
        var reads = shared.Gets.Count;

        invalidator.Invalidate([CacheChange.Actor(Owner)]);
        await ReadActorAsync(Owner);

        // Not started: Valkey still has it, but this instance had to ask again.
        Assert.Equal(reads + 1, shared.Gets.Count);
    }

    [Fact]
    public async Task DeletesTheEntriesFromValkeyAndTellsEveryInstance()
    {
        await invalidator.StartAsync(stop.Token);
        shared.Put(CacheKinds.Following, Owner, (IReadOnlyList<Guid>)[Agent]);

        invalidator.Invalidate([CacheChange.Following(Owner), CacheChange.Handle("tom.claude")]);

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Following.Key(Owner), CacheKinds.Handle.Key("tom.claude")], message.Keys.Order());
        Assert.Empty(message.PageTags);
        Assert.False(shared.Entries.ContainsKey(CacheKinds.Following.Key(Owner)));
    }

    [Fact]
    public async Task RunsEveryChangeASecondTimeForReadsThatRacedTheFirst()
    {
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.Beep(Root)]);

        await TestSupport.WaitUntilAsync(() => shared.Deletes.Count(d => d.Contains(CacheKinds.Beep.Key(Root))) == 2);
    }

    [Fact]
    public async Task ANewBeepEvictsThePagesItBelongsOnItsThreadItsAuthorsAndOwnersProfilesItsHashtagsAndThePublicTimeline()
    {
        await invalidator.StartAsync(stop.Token);
        shared.Put(CacheKinds.Beep, Root, Beep(Root, Agent, null, "hello #Weather and #天气"));
        shared.Put(CacheKinds.Actor, Agent, AgentOf(Owner));
        shared.Put(CacheKinds.Actor, Owner, Human(Owner));

        invalidator.Invalidate([CacheChange.BeepPosted(Root)]);

        var message = await shared.PublishedAsync();
        Assert.Empty(message.Keys);
        Assert.Equal(
            [PageTags.Actor(Owner), PageTags.Actor(Agent), PageTags.Public, PageTags.Hashtag("weather"), PageTags.Hashtag("天气"), PageTags.Thread(Root)],
            message.PageTags.Order(StringComparer.Ordinal));
        // At least once: the second pass, 50 ms later, may already have evicted it again on a busy machine.
        await pages.Received().EvictByTagAsync(PageTags.Thread(Root), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARemovedBeepEvictsItsEntryAndOnlyThePagesShowingItWithoutReadingAnything()
    {
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.BeepRemoved(Root)]);

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Beep.Key(Root)], message.Keys);
        Assert.Equal([PageTags.Beep(Root)], message.PageTags);
        Assert.Empty(shared.Gets);
    }

    [Fact]
    public async Task AHiddenBeepEvictsThePagesShowingItButKeepsItsEntry()
    {
        await invalidator.StartAsync(stop.Token);
        shared.Put(CacheKinds.Beep, Reply, Beep(Reply, Owner, Root, "a reply"));

        invalidator.Invalidate([CacheChange.BeepHidden(Reply)]);

        var message = await shared.PublishedAsync();
        Assert.Empty(message.Keys);
        Assert.Equal([PageTags.Beep(Reply)], message.PageTags);
        Assert.True(shared.Entries.ContainsKey(CacheKinds.Beep.Key(Reply)));
    }

    [Fact]
    public async Task ChangedCountsEvictThePagesShowingTheBeepAndNothingElse()
    {
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.BeepCounts(Root)]);

        var message = await shared.PublishedAsync();
        Assert.Empty(message.Keys);
        Assert.Equal([PageTags.Beep(Root)], message.PageTags);
    }

    [Fact]
    public async Task ARenamedActorEvictsThePagesShowingItAndDropsTheAccountState()
    {
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.Actor(Agent)]);

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Actor.Key(Agent)], message.Keys);
        Assert.Equal([PageTags.Shows(Agent)], message.PageTags);
        Assert.Empty(shared.Gets);
        await hybrid.Received().RemoveAsync(Arg.Is<IEnumerable<string>>(keys => keys.Single() == SharedCache.AccountKey(Agent)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASignInEntryIsDroppedAtOnceAndAgainForReadsThatRacedTheRevocation()
    {
        await invalidator.StartAsync(stop.Token);

        await invalidator.ForgetSharedAsync(["session:abc"]);

        await hybrid.Received(1).RemoveAsync(Arg.Is<IEnumerable<string>>(keys => keys.Single() == "session:abc"), Arg.Any<CancellationToken>());
        await TestSupport.WaitUntilAsync(() => RemovalsOf("session:abc") == 2);
        // Nothing for the other instances: no instance holds a copy of these.
        Assert.Empty(shared.Published);
    }

    [Fact]
    public async Task ASignInEntryThatValkeyCouldNotDropIsDroppedOnceItIsBack()
    {
        var failures = 1;
        hybrid.When(h => h.RemoveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).AsTask())
            .Do(_ =>
            {
                if (failures-- > 0)
                {
                    throw new InvalidOperationException("Valkey is down");
                }
            });
        await invalidator.StartAsync(stop.Token);

        // The revocation is committed: the caller is not failed for the cache.
        await invalidator.ForgetSharedAsync(["agentkey:abc"]);

        await TestSupport.WaitUntilAsync(() => RemovalsOf("agentkey:abc") >= 3);
        // And stored, in case this instance stops before Valkey is back.
        outbox.Received(1).Enqueue(
            SharedCacheForgetRequest.MessageType,
            Arg.Is<SharedCacheForgetRequest>(r => r.Keys.Single() == "agentkey:abc"),
            Arg.Any<JsonTypeInfo<SharedCacheForgetRequest>>());
    }

    [Fact]
    public async Task TheStoredRetryOfASignInEntryDropsIt()
    {
        var message = TestData.Envelope(SharedCacheForgetRequest.MessageType, new SharedCacheForgetRequest(["session:abc"]), ContentCacheJsonContext.Default.SharedCacheForgetRequest);

        await new SharedCacheForgetHandler(hybrid).HandleAsync(message, TestSupport.Ct);

        await hybrid.Received(1).RemoveAsync(Arg.Is<IEnumerable<string>>(keys => keys.Single() == "session:abc"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConnectedAgentEvictsItsOwnersProfileWhereItNowAppears()
    {
        await invalidator.StartAsync(stop.Token);
        shared.Put(CacheKinds.Actor, Agent, AgentOf(Owner));
        shared.Put(CacheKinds.Actor, Owner, Human(Owner));

        invalidator.Invalidate([CacheChange.AgentConnected(Agent)]);

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Actor.Key(Agent)], message.Keys);
        Assert.Equal([PageTags.Actor(Owner)], message.PageTags);
    }

    [Fact]
    public async Task ABanEvictsThePagesShowingTheHumanOrTheirAgents()
    {
        await invalidator.StartAsync(stop.Token);
        shared.Put(CacheKinds.Agents, Owner, (IReadOnlyList<Guid>)[Agent]);

        invalidator.Invalidate([CacheChange.ActorVisibility(Owner)]);

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Actor.Key(Owner)], message.Keys);
        Assert.Equal([PageTags.Shows(Owner), PageTags.Shows(Agent)], message.PageTags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LiftingABanEvictsEveryCachedPage()
    {
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.ActorRestored(Owner)]);

        var message = await shared.PublishedAsync();
        Assert.Equal([PageTags.All], message.PageTags);
        Assert.Empty(shared.Gets);
    }

    [Fact]
    public async Task TheOutboxCopyRunsAtOnceAndFailsForARetry()
    {
        var handler = new CacheInvalidationHandler(invalidator);
        var message = TestData.Envelope(
            CacheInvalidationRequest.MessageType, new CacheInvalidationRequest([CacheChange.Following(Owner)]), ContentCacheJsonContext.Default.CacheInvalidationRequest);

        // Not started: the handler does the work itself.
        await handler.HandleAsync(message, TestSupport.Ct);

        Assert.Contains(shared.Deletes, d => d.Contains(CacheKinds.Following.Key(Owner)));
        Assert.Single(shared.Published);
        shared.Failure = new InvalidOperationException("Valkey is down");
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(message, TestSupport.Ct));
    }

    [Fact]
    public async Task KeepsTryingWhileValkeyIsAway()
    {
        shared.Failure = new InvalidOperationException("Valkey is down");
        await invalidator.StartAsync(stop.Token);

        invalidator.Invalidate([CacheChange.Agents(Owner)]);
        await Task.Delay(100, TestSupport.Ct);
        shared.Failure = null;

        await TestSupport.WaitUntilAsync(() => shared.Deletes.Any(d => d.Contains(CacheKinds.Agents.Key(Owner))));
    }

    [Fact]
    public async Task TheListenerAppliesWhatAnyInstancePublishes()
    {
        var listener = new CacheInvalidationListener(shared, cache, pages, TimeProvider.System, NullLogger<CacheInvalidationListener>.Instance);
        await listener.StartAsync(stop.Token);
        await TestSupport.WaitUntilAsync(() => EvictionsOf(PageTags.All) == 1);
        shared.Put(CacheKinds.Actor, Owner, Human(Owner));
        await ReadActorAsync(Owner);
        var reads = shared.Gets.Count;

        // As another instance would: Valkey's entry stays, only the message arrives.
        await shared.PublishAsync(JsonSerializer.SerializeToUtf8Bytes(
            new CacheInvalidationMessage([CacheKinds.Actor.Key(Owner)], [PageTags.Actor(Owner)]),
            ContentCacheJsonContext.Default.CacheInvalidationMessage));
        await ReadActorAsync(Owner);

        Assert.Equal(reads + 1, shared.Gets.Count);
        await TestSupport.WaitUntilAsync(() => EvictionsOf(PageTags.Actor(Owner)) == 1);
        await listener.StopAsync(CancellationToken.None);
        listener.Dispose();
    }

    [Fact]
    public async Task TheListenerDropsWhatThisInstanceCachedBeforeItSubscribed()
    {
        shared.Put(CacheKinds.Actor, Owner, Human(Owner));
        await ReadActorAsync(Owner);
        var reads = shared.Gets.Count;
        var listener = new CacheInvalidationListener(shared, cache, pages, TimeProvider.System, NullLogger<CacheInvalidationListener>.Instance);

        // Whatever was published meanwhile never reached this instance.
        await listener.StartAsync(stop.Token);
        await TestSupport.WaitUntilAsync(() => EvictionsOf(PageTags.All) == 1);
        await ReadActorAsync(Owner);

        Assert.Equal(reads + 1, shared.Gets.Count);
        await listener.StopAsync(CancellationToken.None);
        listener.Dispose();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task ASignInEntryLoadedSlowerThanTheSecondPassIsDroppedAgain(bool slow, int removals)
    {
        // HybridCache writes the entry to Valkey after returning it: a real one, over a Valkey that records removals.
        var valkey = Substitute.For<IDistributedCache>();
        await using var hybridServices = new ServiceCollection().AddSingleton(valkey).AddHybridCache().Services.BuildServiceProvider();
        using var withValkey = new CacheInvalidator(
            cache, shared, services.GetRequiredService<IServiceScopeFactory>(), pages, hybridServices.GetRequiredService<HybridCache>(), Options.Create(options), TimeProvider.System, NullLogger<CacheInvalidator>.Instance);
        await withValkey.StartAsync(stop.Token);

        // A session read just before its revocation committed, and still loading after both removals ran.
        var found = await withValkey.GetSharedAsync(
            "session:abc",
            slow ? options.SecondPassDelay * 2 : TimeSpan.Zero,
            static async (delay, ct) =>
            {
                await Task.Delay(delay, ct);
                return "live";
            },
            SharedCache.Session);

        Assert.Equal("live", found);
        await Task.Delay(options.SecondPassDelay * 4, TestSupport.Ct);
        await TestSupport.WaitUntilAsync(() => valkey.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IDistributedCache.RemoveAsync)) == removals);
        await withValkey.StopAsync(CancellationToken.None);
    }

    private int EvictionsOf(string tag) =>
        pages.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IOutputCacheStore.EvictByTagAsync) && Equals(c.GetArguments()[0], tag));

    private int RemovalsOf(string key) =>
        hybrid.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(HybridCache.RemoveAsync) && c.GetArguments()[0] is IEnumerable<string> keys && keys.Contains(key));

    private static CachedActor Human(Guid id) => new(id, ActorKind.Human, "tom", "Tom", null, null, null, null, TestData.Now, Deleted: false, Banned: false, BannerKey: null, Website: null);

    private static CachedActor AgentOf(Guid owner) => new(Agent, ActorKind.Agent, "tom.claude", "tom's Claude", owner, null, null, "Claude", TestData.Now, Deleted: false, Banned: false, BannerKey: null, Website: null);

    private static CachedBeep Beep(Guid id, Guid author, Guid? replyTo, string text) =>
        new(id, text, author, replyTo, replyTo ?? id, TestData.Now, 0, 0, [], Array.Empty<MediaRow>());

    private Task<IReadOnlyDictionary<Guid, CachedActor>> ReadActorAsync(Guid id) =>
        cache.GetManyAsync(CacheKinds.Actor, [id], (_, _) => throw new InvalidOperationException("Not in the cache."), TestSupport.Ct);
}
