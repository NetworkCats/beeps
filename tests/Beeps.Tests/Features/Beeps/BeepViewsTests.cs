using Beeps.Features.Accounts;
using Beeps.Features.Avatars;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepViewsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Viewer Agent = Viewer.Agent(Guid.NewGuid(), OwnerId);

    private readonly IBeepStore store = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();

    [Fact]
    public async Task ViewsCombineRowsMentionsAndLiveCounts()
    {
        var first = Row("hello #Beeps", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null), likes: 1, replies: 1);
        var second = Row("plain", new ActorRow(Agent.ActorId, ActorKind.Agent, "tom.claude", "tom's Claude", OwnerId), likes: 5, replies: 0);
        var mentioned = new ActorRow(Guid.NewGuid(), ActorKind.Agent, "tom.gpt", "tom's GPT", OwnerId);
        store.GetMentionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<ActorRow>> { [first.Id] = [mentioned] });
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, BeepCounts> { [first.Id] = new(3, -1) });

        var views = await Create().BuildAsync([first, second], Agent, TestSupport.Ct);

        Assert.Equal([first.Id, second.Id], views.Select(v => v.Id));
        Assert.Equal(Relations.Owner, views[0].Author.Relation);
        Assert.Equal(Relations.Self, views[1].Author.Relation);
        Assert.Equal((3, 0), (views[0].LikeCount, views[0].ReplyCount));
        Assert.Equal((5, 0), (views[1].LikeCount, views[1].ReplyCount));
        Assert.Equal(Relations.Sibling, Assert.Single(views[0].Mentions).Relation);
        Assert.Empty(views[1].Mentions);
        Assert.Equal(["beeps"], views[0].Hashtags);
        Assert.Equal(new Uri($"https://beeps.test/avatars/{OwnerId:D}.svg"), views[0].Author.AvatarUrl);
    }

    [Fact]
    public async Task WithoutValkeyTheColumnsAreShown()
    {
        var row = Row("hi", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null), likes: 2, replies: 4);
        store.GetMentionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyDictionary<Guid, BeepCounts>>(_ => throw new InvalidOperationException("down"));

        var page = await Create().BuildPageAsync(new CursorPage<BeepRow>([row], row.Id), null, TestSupport.Ct);

        Assert.Equal((2, 4), (page.Items[0].LikeCount, page.Items[0].ReplyCount));
        Assert.Equal(row.Id, page.NextCursor);
        Assert.Equal(Relations.Other, page.Items[0].Author.Relation);
    }

    [Fact]
    public async Task ViewersMayDeleteTheirOwnBeepsAndAHumanTheirAgentsToo()
    {
        var byOwner = Row("mine", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null), likes: 0, replies: 0);
        var byAgent = Row("my agent's", new ActorRow(Agent.ActorId, ActorKind.Agent, "tom.claude", "tom's Claude", OwnerId), likes: 0, replies: 0);
        var bySibling = Row("sibling's", new ActorRow(Guid.NewGuid(), ActorKind.Agent, "tom.gpt", "tom's GPT", OwnerId), likes: 0, replies: 0);
        store.GetMentionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts>());
        var views = Create();

        var asOwner = await views.BuildAsync([byOwner, byAgent, bySibling], Viewer.Human(OwnerId), TestSupport.Ct);
        var asAgent = await views.BuildAsync([byOwner, byAgent, bySibling], Agent, TestSupport.Ct);
        var signedOut = await views.BuildAsync([byOwner, byAgent, bySibling], null, TestSupport.Ct);

        Assert.Equal([true, true, true], asOwner.Select(b => b.CanDelete));
        Assert.Equal([false, true, false], asAgent.Select(b => b.CanDelete));
        Assert.Equal([false, false, false], signedOut.Select(b => b.CanDelete));
    }

    [Fact]
    public async Task NoRowsNeedNoLookups()
    {
        var views = await Create().BuildAsync([], Agent, TestSupport.Ct);

        Assert.Empty(views);
        await store.DidNotReceiveWithAnyArgs().GetMentionsAsync(default!, TestSupport.Ct);
        await counters.DidNotReceiveWithAnyArgs().GetAsync(default!, TestSupport.Ct);
    }

    private static BeepRow Row(string text, ActorRow author, int likes, int replies)
    {
        var id = Guid.CreateVersion7();
        return new BeepRow(id, text, author, null, id, TestData.Now, likes, replies, false);
    }

    private BeepViews Create() => new(store, counters, new AvatarUrls(TestData.Site), NullLogger<BeepViews>.Instance);
}
