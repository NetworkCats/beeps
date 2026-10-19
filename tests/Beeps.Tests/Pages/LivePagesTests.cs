using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Live;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Search;
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
        var page = await Islands.PageAsync(client, "/");

        Assert.Matches("<script type=\"module\" src=\"/dist/app\\.[^\"]+\"></script>", html);
        // The render time lets the stream replay what happened before the script connected.
        Assert.Matches($"<body class=\"page\" data-auth=\"member:{user.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Equal("home", Islands.Props(page, "index").GetProperty("live").GetProperty("watch").GetString());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheAppIsToldWhereItsTextIsAndWhoIsSignedIn()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var root = Islands.Config(await Islands.PageAsync(client, "/"));

        // Only where the text is: it is fetched apart and kept by the browser (ClientStringsTests).
        Assert.Matches("^/strings/en\\.json\\?v=[0-9a-f]{10}$", root.GetProperty("stringsUrl").GetString());
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
        var page = await Islands.PageAsync(client, "/public");

        // Connected, so the tab follows its verification elsewhere, but nothing on the page is followed.
        Assert.Matches($"<body class=\"page\" data-auth=\"unverified:{newbie.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Contains("/dist/app.", html, StringComparison.Ordinal);
        Assert.False(Islands.Props(page, "public").GetProperty("live").TryGetProperty("watch", out _));
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

        var first = await Islands.PageAsync(client, "/public");
        var older = await Islands.PageAsync(client, $"/public?before={Guid.NewGuid()}");
        var lastPage = await Islands.PageAsync(client, $"/beeps/{root.Id}");
        var notLast = await Islands.PageAsync(client, $"/beeps/{root.Id}");

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

        var profile = await Islands.PageAsync(client, "/@alice/replies");
        var tag = await Islands.PageAsync(client, "/tags/%23Beeps");
        var inbox = await Islands.PageAsync(client, "/notifications");

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

        var list = await Islands.PageAsync(client, "/agents");
        using var created = await WebAuth.PostFormAsync(client, "/agents", "/agents?handler=Create");
        var withKey = await created.Content.ReadAsStringAsync(TestSupport.Ct);
        var agentId = Islands.Props(withKey, "agents").GetProperty("newKey").GetProperty("agentId").GetString()!;
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns([new AgentRow(Guid.Parse(agentId), null, null, null, TestData.Now, null, "beeps_abc", TestData.Now, null)]);
        var waiting = await Islands.PageAsync(client, $"/onboarding/connect/{agentId}");
        var again = await Islands.PageAsync(client, "/agents");

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
        factory.Profiles.ListFollowedCachedAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
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
    public async Task ABeepsPageSaysItWasViewedWhenAnyoneIsSignedInWhichReadsWhatIsAboutIt()
    {
        // Not verified yet is still signed in.
        var newbie = WebAuth.ArrangeUser(factory, "newbie", verified: false);
        using var client = await WebAuth.SignInAsync(factory, newbie);
        using var anonymous = WebAuth.NewClient(factory);
        var beep = TestBeeps.Row("hi", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        var token = await WebAuth.TokenAsync(client, "/public");
        var url = new Uri($"/live/beeps/{beep.Id:D}/view", UriKind.Relative);

        using var viewed = await client.PostAsync(url, new FormUrlEncodedContent([KeyValuePair.Create("__RequestVerificationToken", token)]), TestSupport.Ct);
        using var forged = await client.PostAsync(url, new FormUrlEncodedContent([]), TestSupport.Ct);
        using var signedOut = await anonymous.PostAsync(url, new FormUrlEncodedContent([]), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NoContent, viewed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        await factory.Beeps.Received(1).RecordViewAsync(newbie.Id, beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await factory.Notifications.Received(1).MarkSeenAsync(newbie.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageSaysWhichBeepsItHadOnScreen()
    {
        var member = WebAuth.ArrangeUser(factory, "tom");
        using var client = await WebAuth.SignInAsync(factory, member);
        var token = await WebAuth.TokenAsync(client, "/public");
        var (one, two) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var url = new Uri("/live/notifications/seen", UriKind.Relative);
        FormUrlEncodedContent Form(string ids) => new([KeyValuePair.Create("__RequestVerificationToken", token), KeyValuePair.Create("ids", ids)]);

        using var seen = await client.PostAsync(url, Form($"{one:D},{two:D}"), TestSupport.Ct);
        using var garbled = await client.PostAsync(url, Form("nope"), TestSupport.Ct);
        using var tooMany = await client.PostAsync(url, Form(string.Join(',', Enumerable.Range(0, NotificationService.MaxSeenBeeps + 1).Select(_ => Guid.NewGuid()))), TestSupport.Ct);
        using var forged = await client.PostAsync(url, new FormUrlEncodedContent([KeyValuePair.Create("ids", $"{one:D}")]), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], new[] { garbled.StatusCode, tooMany.StatusCode, forged.StatusCode });
        await factory.Notifications.Received(1).MarkSeenAsync(member.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { one, two })), null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await factory.Notifications.ReceivedWithAnyArgs(1).MarkSeenAsync(default, default!, default, default, TestSupport.Ct);
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

        var html = await client.GetStringAsync(new Uri("/settings", UriKind.Relative), TestSupport.Ct);
        var settings = await Islands.PageAsync(client, "/settings");
        using var response = await client.GetAsync(new Uri("/live/viewer", UriKind.Relative), TestSupport.Ct);

        // The navigation's account menus show the avatar, and the settings page its own, which follows the account.
        Assert.Equal(2, Islands.Names(html).Count(name => name == "account-menu"));
        Assert.Equal([user.Id.ToString("D")], Islands.Strings(Islands.Props(settings, "settings").GetProperty("live").GetProperty("actors")));
        var viewer = await Islands.JsonAsync(response);
        Assert.Equal(user.Id, viewer.GetProperty("id").GetGuid());
        Assert.EndsWith(".svg", viewer.GetProperty("avatar").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMentionBeingTypedIsAnsweredWithPeople()
    {
        var user = WebAuth.ArrangeUser(factory);
        var alice = TestBeeps.Human("alice");
        factory.SearchStore.SuggestActorsAsync("al", Arg.Is<Viewer>(v => v.ActorId == user.Id), SearchService.MaxSuggestions, Arg.Any<CancellationToken>())
            .Returns([alice]);
        using var anonymous = WebAuth.NewClient(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/live/mentions?q=Al", UriKind.Relative), TestSupport.Ct);
        using var signedOut = await anonymous.GetAsync(new Uri("/live/mentions?q=al", UriKind.Relative), TestSupport.Ct);

        var person = (await Islands.JsonAsync(response)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(alice.Id, person.GetProperty("id").GetGuid());
        Assert.Equal("alice", person.GetProperty("handle").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
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
        var signedOut = await anonymous.GetStringAsync(new Uri("/@tom.renamed", UriKind.Relative), TestSupport.Ct);
        var signedIn = await Islands.PageAsync(member, "/@tom.renamed");

        Assert.Equal(HttpStatusCode.Redirect, byId.StatusCode);
        Assert.Equal($"/@tom.renamed/replies?before={Guid.Empty:D}", byId.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        var live = Islands.Props(signedIn, "profile").GetProperty("live");
        Assert.Equal($"actor:{agent.Id:D}", live.GetProperty("subject").GetString());
        Assert.Equal($"/actors/{agent.Id:D}", live.GetProperty("url").GetString());
        // Signed-out pages are cached for everyone: the same markup, following nothing live.
        Assert.Contains("<div class=\"profile-card\">", signedOut, StringComparison.Ordinal);
        Assert.False(Islands.Props(signedOut, "profile").GetProperty("live").TryGetProperty("watch", out _));
    }

    [Fact]
    public async Task APagesDataIsJsonAtItsAddressUnderPageData()
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
        Assert.False(string.IsNullOrEmpty(page.GetProperty("title").GetString()));
        var islands = page.GetProperty("islands");
        Assert.Equal(["public"], islands.EnumerateObject().Select(p => p.Name));
        Assert.Equal(beep.Id, islands.GetProperty("public").GetProperty("props").GetProperty("list").GetProperty("items")[0].GetProperty("id").GetGuid());
        // An address of its own: nothing to vary by, and never kept.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Empty(response.Headers.Vary);
        // Signed out too (a list's next page): the same props, nothing personal.
        var signedOutPage = await Islands.JsonAsync(signedOut);
        Assert.False(signedOutPage.GetProperty("islands").GetProperty("public").GetProperty("props").GetProperty("live").TryGetProperty("watch", out _));
        Assert.Equal("", signedOutPage.GetProperty("page").GetProperty("config").GetProperty("antiforgery").GetProperty("token").GetString());
        Assert.False(signedOutPage.GetProperty("page").TryGetProperty("auth", out _));
    }

    [Fact]
    public async Task APagesDataSaysAllTheAppNeedsToShowIt()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("hello", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], null));
        using var member = await WebAuth.SignInAsync(factory, user);

        using var response = await Islands.GetAsync(member, "/public");
        using var search = await Islands.GetAsync(member, "/search");

        var answer = await Islands.JsonAsync(response);
        Assert.Equal(beep.Id, answer.GetProperty("islands").GetProperty("public").GetProperty("props").GetProperty("list").GetProperty("items")[0].GetProperty("id").GetGuid());
        var page = answer.GetProperty("page");
        // The content column with its island empty, no skeleton and no props in it: those come as JSON beside it.
        Assert.Equal("<div class=\"island\" data-island=\"public\"></div>", page.GetProperty("main").GetString()!.Trim());
        Assert.Equal("en", page.GetProperty("lang").GetString());
        Assert.Equal($"member:{user.Id:D}", page.GetProperty("auth").GetString());
        Assert.True(page.GetProperty("liveFrom").GetInt64() > 0);
        Assert.Equal("tom", page.GetProperty("config").GetProperty("viewer").GetProperty("handle").GetString());
        Assert.False(string.IsNullOrEmpty(page.GetProperty("config").GetProperty("antiforgery").GetProperty("token").GetString()));
        var shell = page.GetProperty("shell");
        Assert.Equal("public", shell.GetProperty("nav").GetString());
        Assert.Empty(shell.GetProperty("hidden").EnumerateArray());
        Assert.Equal(["main", "member"], Islands.Strings(shell.GetProperty("styles")));
        // The search page has a search box of its own: the side column's is left out.
        var searchShell = (await Islands.JsonAsync(search)).GetProperty("page").GetProperty("shell");
        Assert.Equal("search", searchShell.GetProperty("nav").GetString());
        Assert.Equal(["search"], Islands.Strings(searchShell.GetProperty("hidden")));
    }

    [Fact]
    public async Task AskingForAPagesDataIsOnlyDoneUnderPageData()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var member = await WebAuth.SignInAsync(factory, user);
        using var anonymous = WebAuth.NewClient(factory);

        using var signedOut = await GetMarkedAsync(anonymous, "/public");
        using var signedIn = await GetMarkedAsync(member, "/public");

        // The mark is the server's own: brought along, it is dropped, and the page answers as a page.
        Assert.Equal("text/html", signedOut.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<section class=\"feed\">", await signedOut.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        Assert.Equal("text/html", signedIn.Content.Headers.ContentType?.MediaType);

        static async Task<HttpResponseMessage> GetMarkedAsync(HttpClient client, string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
            request.Headers.Add(Beeps.Pages.Islands.IslandRequests.Header, "1");
            return await client.SendAsync(request, TestSupport.Ct);
        }
    }

    [Fact]
    public async Task SignedInAnAppPageIsItsLayoutAloneAndRunsNothingOfThePage()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);
        var profile = await client.GetStringAsync(new Uri("/@alice", UriKind.Relative), TestSupport.Ct);

        // A skeleton shaped like the page, until the app has its data: no title of its own, no props, no config.
        Assert.Matches("<main id=\"main\" class=\"shell-main\" data-app-shell>\\s*<div class=\"skeleton-page\" data-skeleton=\"feed\"", html);
        Assert.Contains("<title>beeps</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-island=\"public\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"app-config\"", html, StringComparison.Ordinal);
        // The layout is there as ever, its islands with their props.
        Assert.Matches($"<body class=\"page\" data-auth=\"member:{user.Id:D}\" data-live-stream=\"/stream\" data-live-from=\"[0-9]{{13}}\">", html);
        Assert.Equal(2, Islands.Names(html).Count(name => name == "account-menu"));
        Assert.Contains("data-skeleton=\"profile\"", profile, StringComparison.Ordinal);
        await factory.Timelines.DidNotReceiveWithAnyArgs().PublicAsync(default!, default, TestSupport.Ct);
        await factory.Profiles.DidNotReceiveWithAnyArgs().FindByHandleAsync(default!, TestSupport.Ct);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task ARedirectFromAPagesDataLeadsToTheDataOfThePageItLeadsTo()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = Guid.NewGuid();
        using var client = await WebAuth.SignInAsync(factory, user);

        using var anonymous = WebAuth.NewClient(factory);

        using var response = await Islands.GetAsync(client, $"/beeps/{beep:D}/actions");
        using var signedOut = await Islands.GetAsync(anonymous, "/settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/page-data/beeps/{beep:D}", response.Headers.Location?.OriginalString);
        // The sign-in challenge's, which is absolute, too; it comes back to the page itself.
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/page-data/login", signedOut.Headers.Location?.AbsolutePath);
        Assert.Contains("ReturnUrl=%2Fsettings", signedOut.Headers.Location?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageNotFoundAnswersAsDataToo()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await Islands.GetAsync(client, $"/beeps/{Guid.NewGuid():D}");

        // The error page, as JSON: the app shows it in place, and shows the page again once what it is about is back.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var page = await Islands.JsonAsync(response);
        Assert.Empty(page.GetProperty("islands").EnumerateObject());
        Assert.Contains("error-page", page.GetProperty("page").GetProperty("main").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFormSentByTheAppIsAnsweredWithThePageItLeadsTo()
    {
        var user = WebAuth.ArrangeUser(factory);
        var bob = TestBeeps.Human("bob");
        factory.Profiles.FindByHandleAsync("bob", Arg.Any<CancellationToken>()).Returns(new ProfileRow(bob, null, null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(bob.Id, Arg.Any<CancellationToken>()).Returns([]);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var followed = await Islands.PostFormAsync(client, "/@bob", "/@bob/follow?handler=Follow");
        // The browser follows the redirect, which stays with the page's data; this client does not.
        using var back = await client.GetAsync(followed.Headers.Location, TestSupport.Ct);
        using var refused = await Islands.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", ""), ("Bio", ""));

        // Followed: back on the profile, whose props come with what was done.
        Assert.Equal(HttpStatusCode.Redirect, followed.StatusCode);
        Assert.Equal("/page-data/@bob", followed.Headers.Location?.OriginalString);
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
        factory.Profiles.ListFollowedCachedAsync(user.Id, Arg.Any<CancellationToken>()).Returns([]);
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
}
