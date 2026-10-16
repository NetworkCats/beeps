using Beeps.Features.Live;

namespace Beeps.Tests.Features.Live;

public sealed class LiveTopicTests
{
    private static readonly Guid Tom = Guid.NewGuid();
    private static readonly Guid TomsAgent = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();

    [Theory]
    [InlineData("", LiveTopicKind.None)]
    [InlineData(null, LiveTopicKind.None)]
    [InlineData("home", LiveTopicKind.Home)]
    [InlineData("public", LiveTopicKind.Public)]
    [InlineData("notifications", LiveTopicKind.Notifications)]
    public void SimpleTopicsParse(string? text, LiveTopicKind kind)
    {
        Assert.True(LiveTopic.TryParse(text, out var topic));
        Assert.Equal(kind, topic.Kind);
        Assert.Equal(text ?? "", topic.ToString());
    }

    [Fact]
    public void TopicsWithAnArgumentRoundTrip()
    {
        LiveTopic[] topics = [LiveTopic.Profile(Tom, replies: false), LiveTopic.Profile(Tom, replies: true), LiveTopic.Thread(Alice), LiveTopic.ForTag("天气")];

        Assert.All(topics, t =>
        {
            Assert.True(LiveTopic.TryParse(t.ToString(), out var parsed));
            Assert.Equal(t, parsed);
        });
        Assert.Equal($"replies:{Tom:D}", topics[1].ToString());
    }

    [Fact]
    public void TagsAreNormalizedLikeEverywhereElse()
    {
        Assert.True(LiveTopic.TryParse("tag:#Beeps", out var topic));

        Assert.Equal(LiveTopic.ForTag("beeps"), topic);
    }

    [Theory]
    [InlineData("homes")]
    [InlineData("home:1")]
    [InlineData("profile")]
    [InlineData("profile:nope")]
    [InlineData("thread:00000000-0000-0000-0000-000000000000")]
    [InlineData("tag:")]
    [InlineData("tag:a b")]
    public void AnythingElseIsRejected(string text) => Assert.False(LiveTopic.TryParse(text, out _));

    [Fact]
    public void HomeShowsTheFollowedAndRepliesAmongThem()
    {
        var home = new HashSet<Guid> { Tom, Alice };

        Assert.True(LiveTopic.Home.Matches(Posted(TomsAgent, Tom), home));
        Assert.True(LiveTopic.Home.Matches(Posted(Alice, Alice, replyToAccount: Tom), home));
        Assert.False(LiveTopic.Home.Matches(Posted(Alice, Alice, replyToAccount: Guid.NewGuid()), home));
        Assert.False(LiveTopic.Home.Matches(Posted(Guid.NewGuid(), Guid.NewGuid()), home));
    }

    [Fact]
    public void PublicProfileTagAndThreadPickTheirBeeps()
    {
        var root = Guid.NewGuid();
        var top = Posted(TomsAgent, Tom, hashtags: ["beeps"], root: root);
        var reply = Posted(TomsAgent, Tom, replyToAccount: Alice, root: root);

        Assert.True(LiveTopic.Public.Matches(top, null));
        Assert.False(LiveTopic.Public.Matches(reply, null));
        Assert.True(LiveTopic.Profile(Tom, replies: false).Matches(top, null));
        Assert.True(LiveTopic.Profile(TomsAgent, replies: false).Matches(top, null));
        Assert.False(LiveTopic.Profile(Tom, replies: false).Matches(reply, null));
        Assert.True(LiveTopic.Profile(Tom, replies: true).Matches(reply, null));
        Assert.False(LiveTopic.Profile(Alice, replies: true).Matches(reply, null));
        Assert.True(LiveTopic.ForTag("beeps").Matches(top, null));
        Assert.False(LiveTopic.ForTag("beep").Matches(top, null));
        Assert.True(LiveTopic.Thread(root).Matches(reply, null));
        Assert.False(LiveTopic.Thread(Guid.NewGuid()).Matches(reply, null));
        Assert.False(LiveTopic.None.Matches(top, null));
        Assert.False(LiveTopic.Notifications.Matches(top, null));
    }

    private static BeepPostedEvent Posted(Guid author, Guid account, Guid? replyToAccount = null, IReadOnlyList<string>? hashtags = null, Guid? root = null)
    {
        var id = Guid.NewGuid();
        return new BeepPostedEvent(id, author, account, replyToAccount is null ? null : Guid.NewGuid(), replyToAccount, root ?? id, hashtags ?? []);
    }
}
