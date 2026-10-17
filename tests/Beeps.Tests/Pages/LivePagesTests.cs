using System.Net;
using Beeps.Features.Accounts;
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
        Assert.Matches($"<body class=\"page\" data-auth=\"member:{user.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Contains("data-live-watch=\"home\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"indicator-count nav-count\" data-inbox-count hidden></span>", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task AnUnverifiedAccountsPagesFollowOnlyTheAccount()
    {
        var newbie = WebAuth.ArrangeUser(factory, "newbie", verified: false);
        using var client = await WebAuth.SignInAsync(factory, newbie);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        // Connected, so the tab follows its verification elsewhere, but nothing on the page is followed.
        Assert.Matches($"<body class=\"page\" data-auth=\"unverified:{newbie.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Contains("/dist/live", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live-watch", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live-region=\"list\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedOutPagesCarryNothingLive()
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<body class=\"page\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-auth", html, StringComparison.Ordinal);
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
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([root], null));
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
        // Beeps in it: the empty state is there for when they go, hidden.
        Assert.Contains("class=\"empty empty-bordered\" hidden=\"hidden\" data-live-empty=\"\">", first, StringComparison.Ordinal);
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
        Assert.Contains($"data-live-region=\"profile\" data-live-actors=\"{alice.Id:D}\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-live-watch=\"tag:beeps\"", tag, StringComparison.Ordinal);
        Assert.Contains("data-live-watch=\"notifications\"", inbox, StringComparison.Ordinal);
        // Nothing in them: the empty state shows, marked for the live script to hide once something comes.
        Assert.Contains("class=\"empty empty-bordered\" data-live-empty=\"\">", profile, StringComparison.Ordinal);
        Assert.Contains("class=\"empty empty-bordered\" data-live-empty=\"\">", inbox, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentsAndTheWaitingPageRedrawWhenAnAgentChangesButANewKeyStays()
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

        Assert.Contains($"data-live-region=\"agents\" data-live-actors=\"{user.Id:D}\"", list, StringComparison.Ordinal);
        // The key is shown only on this response, so it is outside what a live update draws again.
        Assert.True(withKey.IndexOf("beeps_", StringComparison.Ordinal) is >= 0 and var key && key < withKey.IndexOf("data-live-region=\"agents\"", StringComparison.Ordinal));
        Assert.Contains($"data-live-region=\"connect\" data-live-actors=\"{agentId}\"", waiting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMembersTabFollowsItsPageLive()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Profiles.ListFollowedAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        factory.Notifications.CountUnreadAsync(user.Id, NotificationService.HumanUnreadCountLimit, Arg.Any<CancellationToken>()).Returns(1);
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
        // Not verified yet is still signed in: such a tab follows its account (verified elsewhere, signed out).
        using var notVerified = await newbie.GetAsync(new Uri("/stream", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestSupport.Ct);
        using var badWatch = await tom.GetAsync(new Uri("/stream?watch=everything", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fragment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, notVerified.StatusCode);
        Assert.Equal("text/event-stream", notVerified.Content.Headers.ContentType?.MediaType);
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

        Assert.StartsWith($"<li class=\"notification notification-unread\" data-notification=\"{notification.Id:D}\" data-from=\"{alice.Id:D}\">", html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2Fnotifications", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedBeepsComeAsCardsDrawnAsThePageDrawsThem()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        var root = TestBeeps.Row("root", alice);
        var reply = TestBeeps.Row("reply", TestBeeps.Human("bob"), replyTo: root.Id, root: root.Id);
        TestBeeps.Arrange(factory, root, reply);
        factory.Beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>> { [reply.Id] = [alice] });
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/live/beeps?watch=thread:{root.Id:D}&ids={reply.Id:D},{Guid.NewGuid():D}&focus={reply.Id:D}", UriKind.Relative), TestSupport.Ct);
        using var bad = await client.GetAsync(new Uri("/live/beeps?watch=home&ids=nope", UriKind.Relative), TestSupport.Ct);

        // Only the one still visible; marked as the page marks it, saying whom it answers, and naming whom it mentions.
        Assert.Equal(1, html.Split("<article", StringSplitOptions.None).Length - 1);
        Assert.Contains($"<article class=\"beep beep-focused\" id=\"beep-{reply.Id:D}\" data-beep=\"{reply.Id:D}\" data-author=\"{reply.Author.Id:D}\" data-mentions=\"{alice.Id:D}\" data-reply-to=\"{root.Id:D}\">", html, StringComparison.Ordinal);
        Assert.Contains("Replying to @alice", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task TheInboxCatchesUpWithItsWholeFirstPage()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Notifications.ListAsync(user.Id, PageRequest.Create(limit: 30), false, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<NotificationRow>([], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/live/notifications?limit=30", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.Notifications.Received(1).ListAsync(user.Id, PageRequest.Create(limit: 30), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheViewersAvatarIsRedrawnLiveWhereverItShows()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var settings = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"data-live-region=\"nav-avatar\" data-live-actors=\"{user.Id:D}\"", settings, StringComparison.Ordinal);
        Assert.Contains($"data-live-region=\"avatar-preview\" data-live-actors=\"{user.Id:D}\"", settings, StringComparison.Ordinal);
        Assert.Contains($"data-live-region=\"avatar-remove\" data-live-actors=\"{user.Id:D}\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProfileIsFoundByIdAfterARenameAndOnlyMembersPagesSayWhatTheyAreAbout()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agent = TestBeeps.Agent(Guid.NewGuid(), "tom.renamed");
        factory.Profiles.FindHandleAsync(agent.Id, Arg.Any<CancellationToken>()).Returns("tom.renamed");
        factory.Profiles.FindByHandleAsync("tom.renamed", Arg.Any<CancellationToken>()).Returns(new ProfileRow(agent, null, null, TestData.Now, null));
        using var anonymous = WebAuth.NewClient(factory);
        using var member = await WebAuth.SignInAsync(factory, user);

        using var byId = await anonymous.GetAsync(new Uri($"/actors/{agent.Id:D}/replies?before={Guid.Empty:D}", UriKind.Relative), TestSupport.Ct);
        using var gone = await anonymous.GetAsync(new Uri($"/actors/{Guid.NewGuid():D}", UriKind.Relative), TestSupport.Ct);
        var signedOut = await anonymous.GetStringAsync(new Uri("/tom.renamed", UriKind.Relative), TestSupport.Ct);
        var signedIn = await member.GetStringAsync(new Uri("/tom.renamed", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, byId.StatusCode);
        Assert.Equal($"/tom.renamed/replies?before={Guid.Empty:D}", byId.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Contains($"data-live-subject=\"actor:{agent.Id:D}\" data-live-url=\"/actors/{agent.Id:D}\"", signedIn, StringComparison.Ordinal);
        Assert.Contains("data-live-region=\"list\"", signedIn, StringComparison.Ordinal);
        // Signed-out pages are cached for everyone: none of it.
        Assert.DoesNotContain("data-live-subject", signedOut, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live-region=\"list\"", signedOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageWithMoreBeepsThanItCanListAsksForAllAndIsServed()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Profiles.ListFollowedAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = await WebAuth.SignInAsync(factory, user);

        // As live-url.ts writes it.
        using var response = await client.GetAsync(new Uri("/stream?watch=home&all=true", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ATabCanAskHowItIsSignedInAfterItsStreamFailed()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var member = await WebAuth.SignInAsync(factory, user);
        using var anonymous = WebAuth.NewClient(factory);

        using var signedIn = await member.GetAsync(new Uri("/stream/auth", UriKind.Relative), TestSupport.Ct);
        var signedOut = await anonymous.GetStringAsync(new Uri("/stream/auth", UriKind.Relative), TestSupport.Ct);

        // The same text as the page's data-auth.
        Assert.Equal($"member:{user.Id:D}", await signedIn.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal("no-store", signedIn.Headers.CacheControl?.ToString());
        // And when this account may open its next stream (connections per minute are limited).
        Assert.Equal("0", signedIn.Headers.GetValues("X-Stream-Wait-Ms").Single());
        Assert.Equal("anonymous", signedOut);
    }

    public void Dispose() => factory.Dispose();

    [System.Text.RegularExpressions.GeneratedRegex("/onboarding/connect/([0-9a-f-]{36})")]
    private static partial System.Text.RegularExpressions.Regex AgentIdPattern();
}
