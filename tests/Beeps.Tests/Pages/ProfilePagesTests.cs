using System.Net;
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

        var html = await client.GetStringAsync(new Uri("/tom", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<h1 class=\"profile-name\">tom</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"profile-bio\">Line one&#xA;line two</p>", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"actor-row\" href=\"/tom.claude\">", html, StringComparison.Ordinal);
        Assert.Matches("<form method=\"post\" action=\"/tom/follow\\?handler=Follow\">", html);
        Assert.Contains("<a class=\"tab\" href=\"/tom\" aria-current=\"page\">Beeps</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"tab\" href=\"/tom/replies\">Replies</a>", html, StringComparison.Ordinal);
        await factory.Timelines.Received(1).ProfileAsync(tom.Id, ProfileTimeline.Beeps, Arg.Any<PageRequest>(), viewer.Id, Arg.Any<CancellationToken>());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheRepliesTabListsReplies()
    {
        var tom = ArrangeProfile("tom");
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/tom/replies", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"tab\" href=\"/tom/replies\" aria-current=\"page\">Replies</a>", html, StringComparison.Ordinal);
        Assert.Contains("No replies yet", html, StringComparison.Ordinal);
        await factory.Timelines.Received(1).ProfileAsync(tom.Id, ProfileTimeline.Replies, Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAgentsProfileNamesItsOwnerAndModel()
    {
        var owner = TestBeeps.Human("tom");
        var agent = TestBeeps.Agent(owner.Id);
        factory.Profiles.FindByHandleAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(agent, null, "Claude Opus 4.5", TestData.Now, owner));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/tom.claude", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a href=\"/tom\">tom (@tom)</a>", html, StringComparison.Ordinal);
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

        var html = await client.GetStringAsync(new Uri("/tom", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("href=\"/login?returnUrl=%2Ftom\">Follow</a>", html, StringComparison.Ordinal);
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

        var own = await client.GetStringAsync(new Uri("/tom", UriKind.Relative), TestSupport.Ct);
        var agents = await client.GetStringAsync(new Uri("/tom.claude", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"btn btn-sm\" href=\"/settings\">Edit profile</a>", own, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"btn btn-sm\" href=\"/agents/{agent.Id:D}\">Edit profile</a>", agents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProfilesAreNotFoundAndNonHandlesNeverReachTheDatabase()
    {
        using var client = WebAuth.NewClient(factory);

        using var unknown = await client.GetAsync(new Uri("/nobody", UriKind.Relative), TestSupport.Ct);
        using var junk = await client.GetAsync(new Uri("/wp-login.php", UriKind.Relative), TestSupport.Ct);

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

        using var response = await WebAuth.PostFormAsync(client, "/tom", "/tom/follow?handler=Follow");
        factory.Profiles.IsFollowingAsync(viewer.Id, tom.Id, Arg.Any<CancellationToken>()).Returns(true);
        var html = await client.GetStringAsync(new Uri("/tom", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/tom", response.Headers.Location?.OriginalString);
        Assert.Contains("Following. Their beeps and their agents", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/tom/follow?handler=Unfollow\"", html, StringComparison.Ordinal);
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
        using var response = await WebAuth.PostFormAsync(client, "/notifications", "/notifications?handler=Read", ("upToId", notification.Id.ToString()));

        Assert.Contains("<li class=\"notification notification-unread\">", html, StringComparison.Ordinal);
        Assert.Contains("<span>replied to you</span>", html, StringComparison.Ordinal);
        Assert.Contains("hi @tom", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.Notifications.Received(1).MarkReadAsync(user.Id, notification.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task AnEmptyInboxSaysWhatWillShowUp()
    {
        var user = WebAuth.ArrangeUser(factory, locale: "zh-Hans");
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/notifications", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("还没有通知", html, StringComparison.Ordinal);
        Assert.DoesNotContain("handler=Read", html, StringComparison.Ordinal);
    }

    private ActorRow ArrangeProfile(string handle, string? bio = null)
    {
        var actor = TestBeeps.Human(handle);
        factory.Profiles.FindByHandleAsync(handle, Arg.Any<CancellationToken>()).Returns(new ProfileRow(actor, bio, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns([]);
        return actor;
    }

    public void Dispose() => factory.Dispose();
}
