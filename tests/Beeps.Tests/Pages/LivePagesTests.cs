using System.Net;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

/// <summary>Live updates on the web (PLAN 6.9, 6.10): the browsers' stream, what the islands follow, and the props they fetch.</summary>
public sealed partial class LivePagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task MembersPagesLoadTheAppAndSayWhatTheyFollow()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Matches("<script type=\"module\" src=\"/dist/app\\.[^\"]+\"></script>", html);
        Assert.DoesNotContain("src=\"/dist/main.", html, StringComparison.Ordinal);
        // The render time lets the stream replay what happened before the script connected.
        Assert.Matches($"<body class=\"page\" data-auth=\"member:{user.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Equal("home", Islands.Props(html, "index").GetProperty("live").GetProperty("watch").GetString());
        Assert.Contains("<span class=\"island\" data-island=\"inbox-count\">", html, StringComparison.Ordinal);
        Assert.Contains("<script type=\"application/json\" id=\"app-config\">", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheAppIsToldItsTextAndWhoIsSignedIn()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        using var config = JsonDocument.Parse(AppConfigPattern().Match(html).Groups[1].Value);

        var root = config.RootElement;
        Assert.Equal("Delete", root.GetProperty("strings").GetProperty("Beep.Delete").GetString());
        Assert.Equal(Beeps.Pages.Islands.ClientConfig.Keys.Count, root.GetProperty("strings").EnumerateObject().Count());
        Assert.Equal("__RequestVerificationToken", root.GetProperty("antiforgery").GetProperty("field").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("antiforgery").GetProperty("token").GetString()));
        Assert.Equal(user.Id, root.GetProperty("viewer").GetProperty("id").GetGuid());
        Assert.True(root.GetProperty("viewer").GetProperty("member").GetBoolean());
        Assert.Equal(BeepText.MaxLength, root.GetProperty("limits").GetProperty("beepLength").GetInt32());
    }

    [Fact]
    public async Task AnUnverifiedAccountsPagesFollowOnlyTheAccount()
    {
        var newbie = WebAuth.ArrangeUser(factory, "newbie", verified: false);
        using var client = await WebAuth.SignInAsync(factory, newbie);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        // Connected, so the tab follows its verification elsewhere, but nothing on the page is followed.
        Assert.Matches($"<body class=\"page\" data-auth=\"unverified:{newbie.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Contains("/dist/app.", html, StringComparison.Ordinal);
        Assert.False(Islands.Props(html, "public").GetProperty("live").TryGetProperty("watch", out _));
    }

    [Fact]
    public async Task SignedOutPagesAreDrawnByTheServerAndCarryNothingLiveOrPersonal()
    {
        var beep = TestBeeps.Row("worth finding #beeps", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], null));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        // What search engines read is in the page itself, drawn by the server; the app only takes it over.
        var markup = IslandPropsPattern().Replace(html, "");
        Assert.Contains("worth finding <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>", markup, StringComparison.Ordinal);
        Assert.Contains($"<article class=\"beep\" id=\"beep-{beep.Id:D}\">", markup, StringComparison.Ordinal);
        // No live connection, nobody signed in, nothing that would keep the page out of the output cache.
        Assert.Contains("<body class=\"page\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-live", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-auth", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.False(Islands.Props(html, "public").GetProperty("live").TryGetProperty("watch", out _));
        Assert.Contains("src=\"/dist/app.", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
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

        Assert.Equal("public", Islands.Props(first, "public").GetProperty("live").GetProperty("watch").GetString());
        Assert.Equal(root.Id, Islands.Props(first, "public").GetProperty("list").GetProperty("items")[0].GetProperty("id").GetGuid());
        // Counts and removals still apply there.
        Assert.False(Islands.Props(older, "public").GetProperty("live").TryGetProperty("watch", out _));
        var thread = Islands.Props(lastPage, "thread");
        Assert.Equal($"thread:{root.Id:D}", thread.GetProperty("live").GetProperty("watch").GetString());
        Assert.Equal($"beep:{root.Id:D}", thread.GetProperty("live").GetProperty("subject").GetString());
        Assert.Equal(root.Id, thread.GetProperty("focus").GetProperty("id").GetGuid());
        Assert.False(Islands.Props(notLast, "thread").GetProperty("live").TryGetProperty("watch", out _));
        Assert.Equal($"/beeps/{root.Id:D}?after={root.Id:D}", Islands.Props(notLast, "thread").GetProperty("next").GetString());
        // Beeps in it: the empty state comes with them, for when they go.
        Assert.False(string.IsNullOrEmpty(Islands.Props(first, "public").GetProperty("list").GetProperty("emptyTitle").GetString()));
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

        var live = Islands.Props(profile, "profile").GetProperty("live");
        Assert.Equal($"replies:{alice.Id:D}", live.GetProperty("watch").GetString());
        Assert.Equal([alice.Id.ToString("D")], Islands.Strings(live.GetProperty("actors")));
        Assert.Equal($"actor:{alice.Id:D}", live.GetProperty("subject").GetString());
        // Found again by id after a rename.
        Assert.Equal($"/actors/{alice.Id:D}/replies", live.GetProperty("url").GetString());
        Assert.Equal("tag:beeps", Islands.Props(tag, "tag").GetProperty("live").GetProperty("watch").GetString());
        Assert.Equal("notifications", Islands.Props(inbox, "notifications").GetProperty("live").GetProperty("watch").GetString());
        // Nothing in them: the empty state shows.
        var profileList = Islands.Props(profile, "profile").GetProperty("list");
        Assert.Empty(profileList.GetProperty("items").EnumerateArray());
        Assert.False(string.IsNullOrEmpty(profileList.GetProperty("emptyTitle").GetString()));
        Assert.Empty(Islands.Props(inbox, "notifications").GetProperty("page").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task TheAgentsAndTheWaitingPageFollowTheAgentsButANewKeyIsOnlyInItsOwnAnswer()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var list = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);
        using var created = await WebAuth.PostFormAsync(client, "/agents", "/agents?handler=Create");
        var withKey = await created.Content.ReadAsStringAsync(TestSupport.Ct);
        var agentId = Islands.Props(withKey, "agents").GetProperty("newKey").GetProperty("agentId").GetString()!;
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns([new AgentRow(Guid.Parse(agentId), null, null, null, TestData.Now, null, "beeps_abc", TestData.Now, null)]);
        var waiting = await client.GetStringAsync(new Uri($"/onboarding/connect/{agentId}", UriKind.Relative), TestSupport.Ct);
        var again = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal([user.Id.ToString("D")], Islands.Strings(Islands.Props(list, "agents").GetProperty("live").GetProperty("actors")));
        var newKey = Islands.Props(withKey, "agents").GetProperty("newKey");
        Assert.Contains("beeps_", newKey.GetProperty("prompt").GetString(), StringComparison.Ordinal);
        Assert.False(Islands.Props(again, "agents").TryGetProperty("newKey", out _));
        var connect = Islands.Props(waiting, "connect").GetProperty("live");
        Assert.Equal([agentId], Islands.Strings(connect.GetProperty("actors")));
        Assert.Equal($"actor:{agentId}", connect.GetProperty("subject").GetString());
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
    public async Task NewBeepsComeAsPropsForThePageTheyGoOn()
    {
        var user = WebAuth.ArrangeUser(factory);
        var fresh = TestBeeps.Row("just now #beeps", TestBeeps.Human("alice"));
        var newest = Guid.CreateVersion7();
        factory.Timelines.HomeAsync(user.Id, PageRequest.Create(sinceId: newest, limit: LiveFeedService.Limit), user.Id, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<BeepRow>([fresh], null));
        TestBeeps.Arrange(factory, fresh);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri($"/live/beeps?watch=home&after={newest}", UriKind.Relative), TestSupport.Ct);
        using var bad = await client.GetAsync(new Uri($"/live/beeps?watch=notifications&after={newest}", UriKind.Relative), TestSupport.Ct);

        var beep = (await Islands.JsonAsync(response)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(fresh.Id, beep.GetProperty("id").GetGuid());
        Assert.Equal("alice", beep.GetProperty("author").GetProperty("handle").GetString());
        // The text as the server renders it: the one place hashtags and mentions become links.
        Assert.Equal("just now <a class=\"hashtag\" href=\"/tags/beeps\">#beeps</a>", beep.GetProperty("html").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task NewInboxEntriesComeAsProps()
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

        using var response = await client.GetAsync(new Uri($"/live/notifications?after={newest}", UriKind.Relative), TestSupport.Ct);

        var entry = (await Islands.JsonAsync(response)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(notification.Id, entry.GetProperty("id").GetGuid());
        Assert.Equal("reply", entry.GetProperty("type").GetString());
        Assert.Equal(alice.Id, entry.GetProperty("from").GetProperty("id").GetGuid());
        Assert.Equal(reply.Id, entry.GetProperty("beep").GetProperty("id").GetGuid());
        Assert.False(entry.TryGetProperty("read", out _));
    }

    [Fact]
    public async Task ChangedBeepsComeAsPropsLeavingOutThoseNoLongerVisible()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        var root = TestBeeps.Row("root", alice);
        var reply = TestBeeps.Row("reply", TestBeeps.Human("bob"), replyTo: root.Id, root: root.Id);
        TestBeeps.Arrange(factory, root, reply);
        factory.Beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>> { [reply.Id] = [alice] });
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri($"/live/beeps?ids={reply.Id:D},{Guid.NewGuid():D}", UriKind.Relative), TestSupport.Ct);
        using var bad = await client.GetAsync(new Uri("/live/beeps?ids=nope", UriKind.Relative), TestSupport.Ct);

        // Only the one still visible, naming whom it answers and whom it mentions: it is drawn again when they change.
        var beep = (await Islands.JsonAsync(response)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(reply.Id, beep.GetProperty("id").GetGuid());
        Assert.Equal(root.Id, beep.GetProperty("replyToId").GetGuid());
        Assert.Equal([alice.Id.ToString("D")], Islands.Strings(beep.GetProperty("mentions")));
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
    public async Task TheViewerIsFetchedAgainForANewAvatar()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var settings = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        using var response = await client.GetAsync(new Uri("/live/viewer", UriKind.Relative), TestSupport.Ct);

        // The navigation's account menus show the avatar, and the settings page its own, which follows the account.
        Assert.Equal(2, Islands.Names(settings).Count(name => name == "account-menu"));
        Assert.Equal([user.Id.ToString("D")], Islands.Strings(Islands.Props(settings, "settings").GetProperty("live").GetProperty("actors")));
        var viewer = await Islands.JsonAsync(response);
        Assert.Equal(user.Id, viewer.GetProperty("id").GetGuid());
        Assert.EndsWith(".svg", viewer.GetProperty("avatar").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProfileIsFoundByIdAfterARename()
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
        var live = Islands.Props(signedIn, "profile").GetProperty("live");
        Assert.Equal($"actor:{agent.Id:D}", live.GetProperty("subject").GetString());
        Assert.Equal($"/actors/{agent.Id:D}", live.GetProperty("url").GetString());
        // Signed-out pages are cached for everyone: the same markup, following nothing live.
        Assert.Contains("<section class=\"feed profile\">", signedOut, StringComparison.Ordinal);
        Assert.False(Islands.Props(signedOut, "profile").GetProperty("live").TryGetProperty("watch", out _));
    }

    [Fact]
    public async Task AnIslandsRequestAnswersWithThePagesPropsAlone()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("hello", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], null));
        using var member = await WebAuth.SignInAsync(factory, user);
        using var anonymous = WebAuth.NewClient(factory);

        using var response = await Islands.GetAsync(member, "/public");
        using var signedOut = await Islands.GetAsync(anonymous, "/public");

        var page = await Islands.JsonAsync(response);
        Assert.Equal("Public timeline · beeps", page.GetProperty("title").GetString());
        var islands = page.GetProperty("islands");
        Assert.Equal(["public"], islands.EnumerateObject().Select(p => p.Name));
        Assert.Equal(beep.Id, islands.GetProperty("public").GetProperty("props").GetProperty("list").GetProperty("items")[0].GetProperty("id").GetGuid());
        // The same address answers with HTML too: neither may stand in for the other.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains(Beeps.Pages.Islands.IslandRequests.Header, response.Headers.Vary);
        // Signed out too (a list's next page): the same props, nothing personal.
        var signedOutPage = await Islands.JsonAsync(signedOut);
        Assert.False(signedOutPage.GetProperty("islands").GetProperty("public").GetProperty("props").GetProperty("live").TryGetProperty("watch", out _));
    }

    [Fact]
    public async Task AFormSentByTheAppIsAnsweredWithThePageItLeadsTo()
    {
        var user = WebAuth.ArrangeUser(factory);
        var bob = TestBeeps.Human("bob");
        factory.Profiles.FindByHandleAsync("bob", Arg.Any<CancellationToken>()).Returns(new ProfileRow(bob, null, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(bob.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var followed = await Islands.PostFormAsync(client, "/bob", "/bob/follow?handler=Follow");
        // The browser follows the redirect with the same header; this client does not.
        using var back = await Islands.GetAsync(client, followed.Headers.Location!.OriginalString);
        using var refused = await Islands.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", ""), ("Bio", ""));

        // Followed: back on the profile, whose props come with what was done.
        Assert.Equal(HttpStatusCode.Redirect, followed.StatusCode);
        var page = await Islands.JsonAsync(back);
        Assert.False(string.IsNullOrEmpty(page.GetProperty("notice").GetString()));
        Assert.True(page.GetProperty("islands").TryGetProperty("profile", out _));
        // Refused: the same page drawn again, its errors with the island that shows the form.
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var errors = (await Islands.JsonAsync(refused)).GetProperty("islands").GetProperty("settings").GetProperty("errors");
        Assert.True(errors.TryGetProperty("DisplayName", out _));
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

    [System.Text.RegularExpressions.GeneratedRegex("<script data-island-props=\"\" type=\"application/json\">.*?</script>")]
    private static partial System.Text.RegularExpressions.Regex IslandPropsPattern();

    [System.Text.RegularExpressions.GeneratedRegex("<script type=\"application/json\" id=\"app-config\">(.*?)</script>")]
    private static partial System.Text.RegularExpressions.Regex AppConfigPattern();
}
