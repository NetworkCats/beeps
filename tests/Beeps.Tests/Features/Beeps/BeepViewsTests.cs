using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Shared;
using Beeps.Features.Translation;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Beeps;

public sealed class BeepViewsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Viewer Agent = Viewer.Agent(Guid.NewGuid(), OwnerId);

    private readonly IBeepStore store = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();
    private readonly VisitorLanguages visitor = new();

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
    public async Task ImagesAndCustomAvatarsBecomeMediaUrls()
    {
        var withImages = Row("look", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null, AvatarKey: "face"), likes: 0, replies: 0);
        var plain = Row("plain", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null, AvatarKey: "face"), likes: 0, replies: 0);
        store.GetMentionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        store.GetMediaAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<MediaRow>> { [withImages.Id] = [new(Guid.NewGuid(), "first", 800, 600), new(Guid.NewGuid(), "second", 20, 30)] });
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts>());

        var views = await Create().BuildAsync([withImages, plain], null, TestSupport.Ct);

        Assert.Equal(new Uri("https://media.beeps.test/face/avatar.webp"), views[0].Author.AvatarUrl);
        Assert.Equal([new Uri("https://media.beeps.test/first/full.webp"), new Uri("https://media.beeps.test/second/full.webp")], views[0].Media.Select(m => m.Url));
        Assert.Equal(new Uri("https://media.beeps.test/first/thumb.webp"), views[0].Media[0].ThumbnailUrl);
        Assert.Equal((800, 600), (views[0].Media[0].Width, views[0].Media[0].Height));
        Assert.Empty(views[1].Media);
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
    public async Task HumansGetTranslationsButNeverOfTheirOwnBeepsAndAgentsNone()
    {
        var reader = Viewer.Human(OwnerId) with { Languages = new ReaderLanguages("en", []) };
        var translations = new BeepTranslations("ja", "Good morning", "早上好");
        var theirs = Row("おはよう", new ActorRow(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null), likes: 0, replies: 0) with { Translation = translations };
        var own = Row("おはよう", new ActorRow(OwnerId, ActorKind.Human, "tom", "Tom", null), likes: 0, replies: 0) with { Translation = translations };
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts>());

        var read = await Create().BuildAsync([theirs, own], reader, TestSupport.Ct);
        var agent = await Create().BuildAsync([theirs], Agent, TestSupport.Ct);

        Assert.Equal(new TranslationView("ja", "Good morning"), read[0].Translation);
        Assert.Equal("おはよう", read[0].Text);
        Assert.Null(read[1].Translation);
        Assert.Null(agent[0].Translation);
    }

    [Theory]
    [InlineData("en", "Good morning")]
    [InlineData("zh-Hans", "早上好")]
    public async Task SignedOutVisitorsGetTranslationsIntoTheLanguageTheySeeTheSiteIn(string culture, string expected)
    {
        var row = Row("おはよう", new ActorRow(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null), likes: 0, replies: 0) with
        {
            Translation = new BeepTranslations("ja", "Good morning", "早上好"),
        };
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts>());
        visitor.Current = ReaderLanguages.Visitor(culture);

        var read = await Create().BuildAsync([row], null, TestSupport.Ct);

        Assert.Equal(new TranslationView("ja", expected), read[0].Translation);
    }

    [Fact]
    public async Task CrawlersAndWorkOffARequestGetTheBeepsAsWritten()
    {
        var row = Row("おはよう", new ActorRow(Guid.NewGuid(), ActorKind.Human, "alice", "Alice", null), likes: 0, replies: 0) with
        {
            Translation = new BeepTranslations("ja", "Good morning", "早上好"),
        };
        counters.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, BeepCounts>());

        var read = await Create().BuildAsync([row], null, TestSupport.Ct);

        Assert.Null(read[0].Translation);
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

    private BeepViews Create() => new(store, counters, TestData.Avatars, TestData.Media, visitor, NullLogger<BeepViews>.Instance);
}
