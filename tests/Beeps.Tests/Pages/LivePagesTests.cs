using System.Net;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

/// <summary>Live updates on the web (PLAN 6.10): the browsers' stream, what pages tell the script, and the fragments they fetch.</summary>
public sealed partial class LivePagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task MembersPagesLoadTheLiveScriptAndSayWhatTheyShow()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Matches("<script type=\"module\" src=\"/dist/live\\.[^\"]+\"></script>", html);
        // The render time lets the stream replay what happened before the script connected.
        Assert.Matches("<body class=\"page\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{13}\">", html);
        Assert.Contains("data-live-watch=\"home\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"indicator-count nav-count\" data-inbox-count hidden></span>", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task SignedOutPagesCarryNothingLive()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<body class=\"page\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live", html, StringComparison.Ordinal);
        Assert.DoesNotContain("live-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/dist/live", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAListsFirstPageGrowsAndOnlyAThreadsLastPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        var root = TestBeeps.Row("root", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, root);
        factory.Beeps.ListThreadAsync(root.RootId, Arg.Any<Guid?>(), Arg.Any<int>(), user.Id, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<BeepRow>([root], null), new CursorPage<BeepRow>([root], root.Id));
        using var client = await WebAuth.SignInAsync(factory, user);

        var first = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var older = await client.GetStringAsync(new Uri($"/public?before={Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);
        var lastPage = await client.GetStringAsync(new Uri($"/beeps/{root.Id}", UriKind.Relative), TestSupport.Ct);
        var notLast = await client.GetStringAsync(new Uri($"/beeps/{root.Id}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("data-live-watch=\"public\"", first, StringComparison.Ordinal);
        // Counts and removals still apply there.
        Assert.Contains("data-live-watch=\"\"", older, StringComparison.Ordinal);
        Assert.Contains($"data-live-watch=\"thread:{root.Id:D}\"", lastPage, StringComparison.Ordinal);
        Assert.Contains($"data-beep=\"{root.Id:D}\"", lastPage, StringComparison.Ordinal);
        Assert.Contains("data-live-watch=\"\"", notLast, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProfilesTagsAndTheInboxNameTheirTopic()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        factory.Profiles.FindByHandleAsync("alice", Arg.Any<CancellationToken>()).Returns(new ProfileRow(alice, null, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(alice.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var profile = await client.GetStringAsync(new Uri("/alice/replies", UriKind.Relative), TestSupport.Ct);
        var tag = await client.GetStringAsync(new Uri("/tags/%23Beeps", UriKind.Relative), TestSupport.Ct);
        var inbox = await client.GetStringAsync(new Uri("/notifications", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"data-live-watch=\"replies:{alice.Id:D}\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-live-region=\"profile\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-live-watch=\"tag:beeps\"", tag, StringComparison.Ordinal);
        Assert.Contains("data-live-watch=\"notifications\"", inbox, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentsPageReloadsOnAConnectionExceptWhileShowingANewKey()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var list = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);
        using var created = await WebAuth.PostFormAsync(client, "/agents", "/agents?handler=Create");
        var withKey = await created.Content.ReadAsStringAsync(TestSupport.Ct);
        var agentId = AgentIdPattern().Match(withKey).Groups[1].Value;
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns([new AgentRow(Guid.Parse(agentId), null, null, null, TestData.Now, null, "beeps_abc", TestData.Now, null)]);
        var waiting = await client.GetStringAsync(new Uri($"/onboarding/connect/{agentId}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("data-live-agents", list, StringComparison.Ordinal);
        Assert.Contains("beeps_", withKey, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live-agents", withKey, StringComparison.Ordinal);
        Assert.Contains($"data-live-agent=\"{agentId}\"", waiting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMembersTabFollowsItsPageLive()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Profiles.ListFollowedAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        factory.Notifications.CountUnreadAsync(user.Id, Arg.Any<CancellationToken>()).Returns(1);
        using var client = await WebAuth.SignInAsync(factory, user);
        var mine = Guid.NewGuid();

        using var response = await client.GetAsync(new Uri($"/stream?watch=home&from={TestData.Now.ToUnixTimeMilliseconds()}", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestSupport.Ct);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(TestSupport.Ct));
        var inbox = await events.NextAsync();
        await factory.LiveFeed.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct);
        factory.LiveFeed.Publish(LiveEventTypes.BeepPosted, $$"""{"id":"{{Guid.NewGuid():D}}","author_id":"{{Guid.NewGuid():D}}","author_account_id":"{{Guid.NewGuid():D}}","reply_to_id":null,"reply_to_account_id":null,"root_id":"{{Guid.NewGuid():D}}","hashtags":[]}""");
        var id = factory.LiveFeed.Publish(LiveEventTypes.BeepPosted, $$"""{"id":"{{mine:D}}","author_id":"{{user.Id:D}}","author_account_id":"{{user.Id:D}}","reply_to_id":null,"reply_to_account_id":null,"root_id":"{{mine:D}}","hashtags":[]}""");
        var beep = await events.NextAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(("inbox", """{"unread_count":1}"""), (inbox.Type, inbox.Data));
        Assert.Equal(new SseEvent("beep", $$"""{"id":"{{mine:D}}","root_id":"{{mine:D}}","reply_to_id":null}""", id, null), beep);
    }

    [Fact]
    public async Task TheStreamRefusesInsteadOfRedirecting()
    {
        var unverified = WebAuth.ArrangeUser(factory, "newbie", verified: false);
        using var anonymous = WebAuth.NewClient(factory);
        using var newbie = await WebAuth.SignInAsync(factory, unverified);
        var member = WebAuth.ArrangeUser(factory, "tom");
        using var tom = await WebAuth.SignInAsync(factory, member);

        using var signedOut = await anonymous.GetAsync(new Uri("/stream", UriKind.Relative), TestSupport.Ct);
        using var fragment = await anonymous.GetAsync(new Uri("/live/beeps?watch=home", UriKind.Relative), TestSupport.Ct);
        using var notVerified = await newbie.GetAsync(new Uri("/stream", UriKind.Relative), TestSupport.Ct);
        using var badWatch = await tom.GetAsync(new Uri("/stream?watch=everything", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fragment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notVerified.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badWatch.StatusCode);
        Assert.Contains(LiveErrorCodes.InvalidWatch, await badWatch.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewBeepsComeAsCardsForThePageTheyGoOn()
    {
        var user = WebAuth.ArrangeUser(factory);
        var fresh = TestBeeps.Row("just now", TestBeeps.Human("alice"));
        var newest = Guid.CreateVersion7();
        factory.Timelines.HomeAsync(user.Id, PageRequest.Create(sinceId: newest, limit: LiveFeedService.Limit), user.Id, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<BeepRow>([fresh], null));
        TestBeeps.Arrange(factory, fresh);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/live/beeps?watch=home&after={newest}&return=%2F%3Fx%3D1", UriKind.Relative), TestSupport.Ct);
        using var bad = await client.GetAsync(new Uri($"/live/beeps?watch=notifications&after={newest}", UriKind.Relative), TestSupport.Ct);
        var offsite = await client.GetStringAsync(new Uri($"/live/beeps?watch=home&after={newest}&return=https%3A%2F%2Fevil.test", UriKind.Relative), TestSupport.Ct);

        Assert.StartsWith("<article class=\"beep\"", html.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("<html", html, StringComparison.Ordinal);
        Assert.Contains($"data-beep=\"{fresh.Id:D}\"", html, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2F%3Fx%3D1", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("returnUrl=%2F&amp;", offsite, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task NewInboxEntriesComeAsListItems()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        var reply = TestBeeps.Row("hi @tom", alice);
        TestBeeps.Arrange(factory, reply);
        var newest = Guid.CreateVersion7();
        var notification = new NotificationRow(Guid.CreateVersion7(), NotificationType.Reply, alice, reply.Id, TestData.Now, null);
        factory.Notifications.ListAsync(user.Id, PageRequest.Create(sinceId: newest, limit: LiveFeedService.Limit), false, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<NotificationRow>([notification], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/live/notifications?after={newest}", UriKind.Relative), TestSupport.Ct);

        Assert.StartsWith($"<li class=\"notification notification-unread\" data-notification=\"{notification.Id:D}\">", html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2Fnotifications", html, StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();

    [System.Text.RegularExpressions.GeneratedRegex("/onboarding/connect/([0-9a-f-]{36})")]
    private static partial System.Text.RegularExpressions.Regex AgentIdPattern();
}
