using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Features.Shared.Http;
using Beeps.Infrastructure.Caching;
using Beeps.Infrastructure.Data;
using Beeps.Tests.Support;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Beeps.Tests.Infrastructure.Caching;

/// <summary>
/// What tracked changes invalidate. The context is real but never connects: change tracking needs no database,
/// and nothing here saves.
/// </summary>
public sealed class PendingCacheChangesTests : IAsyncDisposable
{
    private static readonly Guid Owner = Guid.CreateVersion7(TestData.Now);
    private static readonly Guid Agent = Guid.CreateVersion7(TestData.Now.AddSeconds(1));

    private readonly InMemorySharedContentCache shared = new();
    private readonly ContentCache cache;
    private readonly ServiceProvider services;
    private readonly CacheInvalidator invalidator;
    private readonly BeepsDbContext db;
    private readonly IOutbox outbox = Substitute.For<IOutbox>();
    private readonly PendingCacheChanges changes;
    private readonly CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);

    public PendingCacheChangesTests()
    {
        var options = Options.Create(new CacheOptions { SecondPassDelay = TimeSpan.FromMinutes(1) });
        cache = new ContentCache(shared, options, TimeProvider.System, NullLogger<ContentCache>.Instance);
        services = new ServiceCollection()
            .AddDbContext<BeepsDbContext>(o => o.UseNpgsql("Host=postgres.invalid;Database=beeps").UseSnakeCaseNamingConvention())
            .AddSingleton(cache)
            .AddScoped<Beeps.Infrastructure.Data.Stores.ContentReads>()
            .BuildServiceProvider();
        invalidator = new CacheInvalidator(
            cache, shared, services.GetRequiredService<IServiceScopeFactory>(), Substitute.For<IOutputCacheStore>(), Substitute.For<HybridCache>(), options, TimeProvider.System, NullLogger<CacheInvalidator>.Instance);
        db = services.GetRequiredService<BeepsDbContext>();
        changes = new PendingCacheChanges(db, invalidator, outbox);
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
    public async Task ARenameDropsTheActorAndItsOldHandle()
    {
        await StartAsync();
        // What the worker reads to find the owner's profile, which lists the agent.
        shared.Put(CacheKinds.Actor, Agent, new CachedActor(Agent, ActorKind.Agent, "tom.claude", "tom's Claude", Owner, null, null, "Claude", TestData.Now, false, false));
        shared.Put(CacheKinds.Actor, Owner, new CachedActor(Owner, ActorKind.Human, "tom", "Tom", null, null, null, null, TestData.Now, false, false));
        var agent = Track(new Actor { Id = Agent, Kind = ActorKind.Agent, OwnerId = Owner, Handle = "tom.claude", NormalizedHandle = "tom.claude", DisplayName = "tom's Claude" });
        agent.Handle = "tom.Helper";
        agent.NormalizedHandle = "tom.helper";

        changes.CaptureTracked();
        changes.Flush();

        var message = await shared.PublishedAsync();
        Assert.Equal([CacheKinds.Actor.Key(Agent), CacheKinds.Handle.Key("tom.claude")], message.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([PageTags.Shows(Agent)], message.PageTags);
    }

    [Fact]
    public async Task SwitchingAnAgentOffChangesNothingCached()
    {
        await StartAsync();
        var agent = Track(new Actor { Id = Agent, Kind = ActorKind.Agent, OwnerId = Owner, Handle = "tom.claude", NormalizedHandle = "tom.claude" });
        agent.DisabledAt = TestData.Now;

        changes.CaptureTracked();
        changes.Flush();

        await Task.Delay(100, TestSupport.Ct);
        Assert.Empty(shared.Published);
    }

    [Fact]
    public async Task ANewAgentDropsItsOwnersAgentList()
    {
        await StartAsync();
        db.Add(new Actor { Id = Agent, Kind = ActorKind.Agent, OwnerId = Owner, CreatedAt = TestData.Now });

        changes.CaptureTracked();
        changes.Flush();

        Assert.Equal([CacheKinds.Agents.Key(Owner)], (await shared.PublishedAsync()).Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABanHidesTheActorAndLiftingItShowsThemAgain(bool lifting)
    {
        var user = Track(new User
        {
            Id = Owner, Actor = new Actor { Id = Owner, Kind = ActorKind.Human, Handle = "tom", DisplayName = "Tom" },
            Email = "t@x.test", NormalizedEmail = "t@x.test", PasswordHash = "x", Locale = "en", BannedAt = lifting ? TestData.Now : null,
        });
        user.BannedAt = lifting ? null : TestData.Now;

        Assert.Equal([lifting ? CacheChange.ActorRestored(Owner) : CacheChange.ActorVisibility(Owner)], Captured());
    }

    [Fact]
    public async Task ARolledBackChangeInvalidatesNothing()
    {
        await StartAsync();
        var actor = Track(new Actor { Id = Owner, Kind = ActorKind.Human, Handle = "tom", DisplayName = "Tom" });
        actor.DisplayName = "Thomas";

        changes.CaptureTracked();
        changes.Discard();
        changes.Flush();

        await Task.Delay(100, TestSupport.Ct);
        Assert.Empty(shared.Published);
    }

    [Fact]
    public void AWriteOutsideATransactionIsRefused()
    {
        // It could not have a durable copy committed with it.
        Assert.Throws<InvalidOperationException>(() => changes.Add(CacheChange.Following(Owner)));
    }

    [Fact]
    public void ANewBeepIsInvalidatedAsPosted()
    {
        db.Add(new Beep { Id = Agent, AuthorId = Owner, RootId = Agent, Text = "hello #news", CreatedAt = TestData.Now });

        Assert.Equal([CacheChange.BeepPosted(Agent)], Captured());
    }

    [Fact]
    public void TheDurableCopyHoldsEverythingPending()
    {
        var actor = Track(new Actor { Id = Owner, Kind = ActorKind.Human, Handle = "tom", DisplayName = "Tom" });
        actor.Bio = "hello";
        changes.CaptureTracked();

        changes.StageDurableCopy();

        outbox.Received(1).Enqueue(
            CacheInvalidationRequest.MessageType,
            Arg.Is<CacheInvalidationRequest>(r => r.Changes.Single() == CacheChange.Actor(Owner)),
            Arg.Any<System.Text.Json.Serialization.Metadata.JsonTypeInfo<CacheInvalidationRequest>>());
    }

    [Fact]
    public void NothingPendingStagesNothing()
    {
        changes.StageDurableCopy();

        Assert.False(changes.HasPending);
        Assert.Empty(outbox.ReceivedCalls());
    }

    private T Track<T>(T entity)
        where T : class
    {
        db.Attach(entity);
        return entity;
    }

    private Task StartAsync() => invalidator.StartAsync(stop.Token);

    /// <summary>What the tracked changes invalidate, from the durable copy staged for them.</summary>
    private IReadOnlyList<CacheChange> Captured()
    {
        changes.CaptureTracked();
        changes.StageDurableCopy();
        return ((CacheInvalidationRequest)Assert.Single(outbox.ReceivedCalls()).GetArguments()[1]!).Changes;
    }
}
