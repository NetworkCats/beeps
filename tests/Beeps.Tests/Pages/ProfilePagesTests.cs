using System.Net;
using System.Text.RegularExpressions;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class ProfilePagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task AHumansProfileShowsTheirAgentsTabsAndAFollowButton()
    {
        var viewer = WebAuth.ArrangeUser(factory, handle: "bob");
        var tom = ArrangeProfile("tom", bio: "Line one\nline two");
        var agent = TestBeeps.Agent(tom.Id);
        factory.Profiles.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([agent]);
        using var client = await WebAuth.SignInAsync(factory, viewer);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);
        var data = await Islands.PageAsync(client, "/@tom");

        var page = Islands.Props(data, "profile");
        var profile = page.GetProperty("profile");
        Assert.Equal("tom", profile.GetProperty("actor").GetProperty("displayName").GetString());
        Assert.Equal("Line one\nline two", profile.GetProperty("bio").GetString());
        Assert.Equal(["tom.claude"], profile.GetProperty("agents").EnumerateArray().Select(a => a.GetProperty("handle").GetString()));
        // Not followed yet, and someone else's: a follow button.
        Assert.False(profile.GetProperty("following").GetBoolean());
        Assert.False(profile.TryGetProperty("mine", out _));
        // The beeps tab, not the replies.
        Assert.False(page.TryGetProperty("replies", out _));
        await factory.Timelines.Received(1).ProfileAsync(tom.Id, ProfileTimeline.Beeps, Arg.Any<PageRequest>(), viewer.Id, Arg.Any<CancellationToken>());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task AWebsiteLeadsOffTheSiteWithoutPassingAnythingOn()
    {
        ArrangeProfile("tom", website: "https://www.tom.example/");
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);

        Assert.Contains(
            "<a class=\"profile-website\" href=\"https://www.tom.example/\" target=\"_blank\" rel=\"nofollow noopener noreferrer ugc\">tom.example</a>",
            html,
            StringComparison.Ordinal);
        var website = Islands.Props(html, "profile").GetProperty("profile").GetProperty("website");
        Assert.Equal(("https://www.tom.example/", "tom.example"), (website.GetProperty("url").GetString(), website.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task TheRepliesTabListsReplies()
    {
        var tom = ArrangeProfile("tom");
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom/replies", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"tab\" href=\"/@tom/replies\" aria-current=\"page\">Replies</a>", html, StringComparison.Ordinal);
        Assert.Contains("No replies yet", html, StringComparison.Ordinal);
        await factory.Timelines.Received(1).ProfileAsync(tom.Id, ProfileTimeline.Replies, Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLikesTabListsWhatTheActorLikedInTheOrderTheyLikedIt()
    {
        var tom = ArrangeProfile("tom");
        var liked = TestBeeps.Row("liked", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, liked);
        factory.Timelines.LikesAsync(tom.Id, Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([liked], liked.Id));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom/likes", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"tab\" href=\"/@tom/likes\" aria-current=\"page\">Likes</a>", html, StringComparison.Ordinal);
        Assert.Contains("liked", html, StringComparison.Ordinal);
        // Older pages by id, so a rename does not lose them; no live topic, as likes are not in the order beeps come.
        Assert.Contains($"href=\"/actors/{tom.Id:D}/likes?before={liked.Id:D}\"", html, StringComparison.Ordinal);
        Assert.False(Islands.Props(html, "profile").GetProperty("live").TryGetProperty("watch", out _));
    }

    [Fact]
    public async Task PrivateLikesHaveNoTabAndAreNotReadForOthers()
    {
        var tom = ArrangeProfile("tom");
        factory.Profiles.GetPrivacyAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(true, false, false));
        using var client = WebAuth.NewClient(factory);

        var profile = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);
        var likes = await client.GetStringAsync(new Uri("/@tom/likes", UriKind.Relative), TestSupport.Ct);

        Assert.DoesNotContain("/@tom/likes", profile, StringComparison.Ordinal);
        Assert.Contains("These likes are private", likes, StringComparison.Ordinal);
        await factory.Timelines.DidNotReceiveWithAnyArgs().LikesAsync(default, default!, default, TestSupport.Ct);
    }

    [Fact]
    public async Task YourOwnPrivateLikesAreStillYours()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(TestBeeps.Human("tom", user.Id), null, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        factory.Profiles.GetPrivacyAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(true, true, true));
        using var client = await WebAuth.SignInAsync(factory, user);

        var page = Islands.Props(await Islands.PageAsync(client, "/@tom/likes"), "profile");

        Assert.Equal("likes", page.GetProperty("tab").GetString());
        Assert.True(page.GetProperty("likes").GetBoolean());
        Assert.True(page.GetProperty("profile").GetProperty("lists").GetBoolean());
        Assert.Equal(0, page.GetProperty("profile").GetProperty("counts").GetProperty("following").GetInt32());
        await factory.Timelines.Received(1).LikesAsync(user.Id, Arg.Any<PageRequest>(), user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheNavigationMarksYourOwnProfileOnly()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(TestBeeps.Human("tom", user.Id), null, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        ArrangeProfile("bob");
        using var client = await WebAuth.SignInAsync(factory, user);

        var own = Islands.Data(await Islands.PageAsync(client, "/@tom")).GetProperty("shell");
        var other = Islands.Data(await Islands.PageAsync(client, "/@bob")).GetProperty("shell");

        Assert.Equal("profile", own.GetProperty("nav").GetString());
        Assert.False(other.TryGetProperty("nav", out _));
    }

    [Fact]
    public async Task AHumansCardCountsTheirFollowsAndLeadsToTheLists()
    {
        var tom = ArrangeProfile("tom");
        factory.Profiles.CountFollowsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(new FollowCounts(12, 3));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);

        Assert.Matches("<a href=\"/@tom/following\">\\s*<strong>12</strong>\\s*Following\\s*</a>", html);
        Assert.Matches("<a href=\"/@tom/followers\">\\s*<strong>3</strong>\\s*Followers\\s*</a>", html);
    }

    [Fact]
    public async Task PrivateCountsAndListsLeaveOnlyWhatIsShown()
    {
        var tom = ArrangeProfile("tom");
        var alice = ArrangeProfile("alice");
        var bob = ArrangeProfile("bob");
        factory.Profiles.GetPrivacyAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(false, false, true));
        factory.Profiles.GetPrivacyAsync(alice.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(false, true, false));
        factory.Profiles.GetPrivacyAsync(bob.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(false, true, true));
        using var client = WebAuth.NewClient(factory);

        var noCounts = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);
        var noLists = await client.GetStringAsync(new Uri("/@alice", UriKind.Relative), TestSupport.Ct);
        var neither = await client.GetStringAsync(new Uri("/@bob", UriKind.Relative), TestSupport.Ct);

        Assert.Matches("<a href=\"/@tom/following\">\\s*Following\\s*</a>", noCounts);
        Assert.Contains("<span><strong>0</strong> Following</span>", noLists, StringComparison.Ordinal);
        Assert.DoesNotContain("/@alice/following", noLists, StringComparison.Ordinal);
        Assert.DoesNotContain("profile-follows", neither, StringComparison.Ordinal);
        await factory.Profiles.DidNotReceive().CountFollowsAsync(tom.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFollowListsShowThePeopleAndPageByThem()
    {
        var tom = ArrangeProfile("tom");
        var alice = TestBeeps.Human("alice");
        factory.Profiles.ListFollowsAsync(tom.Id, FollowList.Followers, Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<PersonRow>([new PersonRow(alice, "hi")], alice.Id));
        using var client = WebAuth.NewClient(factory);

        var followers = Islands.Props(await client.GetStringAsync(new Uri("/@tom/followers", UriKind.Relative), TestSupport.Ct), "follows");
        var following = Islands.Props(await client.GetStringAsync(new Uri("/@tom/following", UriKind.Relative), TestSupport.Ct), "follows");

        Assert.True(followers.GetProperty("followers").GetBoolean());
        var person = Assert.Single(followers.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(("alice", "hi"), (person.GetProperty("actor").GetProperty("handle").GetString(), person.GetProperty("bio").GetString()));
        Assert.Equal($"/actors/{tom.Id:D}/followers?before={alice.Id:D}", followers.GetProperty("page").GetProperty("next").GetString());
        Assert.False(following.TryGetProperty("followers", out _));
        await factory.Profiles.Received(1).ListFollowsAsync(tom.Id, FollowList.Following, Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrivateFollowListsAreNotReadAndAgentsHaveNone()
    {
        var tom = ArrangeProfile("tom");
        factory.Profiles.GetPrivacyAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfilePrivacy(false, true, false));
        factory.Profiles.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(TestBeeps.Agent(tom.Id), null, "Claude", TestData.Now, tom));
        using var client = WebAuth.NewClient(factory);

        var page = Islands.Props(await client.GetStringAsync(new Uri("/@tom/following", UriKind.Relative), TestSupport.Ct), "follows");
        using var agents = await client.GetAsync(new Uri("/@tom.claude/followers", UriKind.Relative), TestSupport.Ct);

        Assert.True(page.GetProperty("hidden").GetBoolean());
        Assert.Empty(page.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, agents.StatusCode);
        await factory.Profiles.DidNotReceiveWithAnyArgs().ListFollowsAsync(default, default, default!, default, TestSupport.Ct);
    }

    [Theory]
    [InlineData("likes", "/@tom/likes")]
    [InlineData("followers", "/@tom/followers")]
    public async Task APageByIdGoesOnToItsAddress(string tab, string address)
    {
        var tom = TestBeeps.Human("tom");
        factory.Profiles.FindHandleAsync(tom.Id, Arg.Any<CancellationToken>()).Returns("tom");
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/actors/{tom.Id:D}/{tab}?before=x", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(address + "?before=x", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AHumanWithMoreThanThreeAgentsListsThreeAndTheRestInADialog()
    {
        var tom = ArrangeProfile("tom");
        var agents = Enumerable.Range(1, 5).Select(n => TestBeeps.Agent(tom.Id, $"tom.bot{n}")).ToList();
        factory.Profiles.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(agents);
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);

        var dialog = html.IndexOf("<dialog", StringComparison.Ordinal);
        Assert.True(dialog > 0);
        Assert.Equal(3, Regex.Count(html[..dialog], "class=\"actor-row\""));
        Assert.Equal(5, Regex.Count(html[dialog..], "class=\"actor-row\""));
        Assert.Contains("commandfor=\"agents-dialog\" command=\"show-modal\">Show more</button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHumanWithThreeAgentsListsThemAll()
    {
        var tom = ArrangeProfile("tom");
        factory.Profiles.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(
            Enumerable.Range(1, 3).Select(n => TestBeeps.Agent(tom.Id, $"tom.bot{n}")).ToList());
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(3, Regex.Count(html, "class=\"actor-row\""));
        Assert.DoesNotContain("<dialog", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAgentsProfileNamesItsOwnerAndModel()
    {
        var owner = TestBeeps.Human("tom");
        var agent = TestBeeps.Agent(owner.Id);
        factory.Profiles.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(agent, null, "Claude Opus 4.5", TestData.Now, owner));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom.claude", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a href=\"/@tom\">tom (@tom)</a>", html, StringComparison.Ordinal);
        Assert.Contains("<dd>Claude Opus 4.5</dd>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"badge\">Agent</span>", html, StringComparison.Ordinal);
        // Agents cannot be followed on their own.
        Assert.DoesNotContain(">Follow<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedOutVisitorsAreOfferedToSignInToFollow()
    {
        ArrangeProfile("tom");
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/@tom", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("href=\"/login?returnUrl=%2F%40tom\">Follow</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__RequestVerificationToken", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YourOwnProfileLinksToYourSettingsAndYourAgentsToTheirPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        var self = TestBeeps.Human("tom", user.Id);
        var agent = TestBeeps.Agent(user.Id);
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(self, null, null, TestData.Now, null));
        factory.Profiles.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(agent, null, "Claude", TestData.Now, self));
        factory.Profiles.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var own = await Islands.PageAsync(client, "/@tom");
        var agents = await Islands.PageAsync(client, "/@tom.claude");

        var ownCard = Islands.Props(own, "profile").GetProperty("profile");
        Assert.True(ownCard.GetProperty("mine").GetBoolean());
        Assert.Equal("/settings/profile", ownCard.GetProperty("editUrl").GetString());
        var agentsCard = Islands.Props(agents, "profile").GetProperty("profile");
        Assert.True(agentsCard.GetProperty("mine").GetBoolean());
        Assert.Equal($"/agents/{agent.Id:D}", agentsCard.GetProperty("editUrl").GetString());
    }

    [Fact]
    public async Task UnknownProfilesAreNotFoundAndNonHandlesNeverReachTheDatabase()
    {
        using var client = WebAuth.NewClient(factory);

        using var unknown = await client.GetAsync(new Uri("/@nobody", UriKind.Relative), TestSupport.Ct);
        using var junk = await client.GetAsync(new Uri("/@wp-login.php", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, junk.StatusCode);
        await factory.Profiles.Received(1).FindByHandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FollowingGoesBackToTheProfileWithANotice()
    {
        var viewer = WebAuth.ArrangeUser(factory, handle: "bob");
        var tom = ArrangeProfile("tom");
        factory.Profiles.AddFollowAsync(viewer.Id, tom.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        using var client = await WebAuth.SignInAsync(factory, viewer);

        using var response = await WebAuth.PostFormAsync(client, "/@tom", "/@tom/follow?handler=Follow");
        factory.Profiles.GetRelationAsync(viewer.Id, tom.Id, Arg.Any<CancellationToken>()).Returns(new ProfileRelation(Following: true, Blocking: false));
        var page = await Islands.PageAsync(client, "/@tom");

        Assert.Equal("/@tom", response.Headers.Location?.OriginalString);
        Assert.Contains("Following. Their beeps and their agents", page, StringComparison.Ordinal);
        // Followed now: the button unfollows.
        Assert.True(Islands.Props(page, "profile").GetProperty("profile").GetProperty("following").GetBoolean());
        await factory.Profiles.Received(1).AddFollowAsync(viewer.Id, tom.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheInboxShowsWhatHappenedAndMarksReadUpToWhatWasShown()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        var reply = TestBeeps.Row("hi @tom", alice);
        TestBeeps.Arrange(factory, reply);
        var notification = new NotificationRow(Guid.CreateVersion7(), NotificationType.Reply, alice, reply.Id, TestData.Now, null);
        factory.Notifications.ListAsync(user.Id, Arg.Any<PageRequest>(), false, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<NotificationRow>([notification], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/notifications", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/notifications");
        using var response = await WebAuth.PostFormAsync(client, "/notifications", "/notifications?handler=Read", ("upToId", notification.Id.ToString()));

        var inbox = Islands.Props(page, "notifications");
        Assert.Equal("notifications", inbox.GetProperty("live").GetProperty("watch").GetString());
        // Unread, and the newest shown: what "mark read" marks read up to.
        var entry = inbox.GetProperty("page").GetProperty("items").EnumerateArray().Single();
        Assert.Equal(notification.Id, entry.GetProperty("id").GetGuid());
        Assert.False(entry.TryGetProperty("read", out _));
        Assert.Equal("reply", entry.GetProperty("type").GetString());
        Assert.Equal("alice", entry.GetProperty("from").GetProperty("handle").GetString());
        Assert.Equal("hi @tom", entry.GetProperty("beep").GetProperty("html").GetString());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.Notifications.Received(1).MarkReadAsync(user.Id, notification.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task ARequestNoHandlerTakesIsRefusedNotDrawnWithoutItsModel()
    {
        ArrangeProfile("tom");
        using var client = WebAuth.NewClient(factory);

        // TRACE passes antiforgery; the page has no handler for it, and drawn anyway it threw on its empty model.
        using var request = new HttpRequestMessage(HttpMethod.Trace, new Uri("/@tom", UriKind.Relative));
        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private ActorRow ArrangeProfile(string handle, string? bio = null, string? website = null)
    {
        var actor = TestBeeps.Human(handle);
        factory.Profiles.FindByHandleAsync(handle, Arg.Any<CancellationToken>()).Returns(new ProfileRow(actor, bio, null, TestData.Now, null, Website: website));
        factory.Profiles.ListAgentsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns([]);
        return actor;
    }

    public void Dispose() => factory.Dispose();
}
