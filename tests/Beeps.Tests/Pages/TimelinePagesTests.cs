using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class TimelinePagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task SignedOutVisitorsSeeTheIntroductionAndThePublicTimeline()
    {
        var tom = TestBeeps.Human();
        var beep = TestBeeps.Row("<b>hi</b> #Beeps @tom\nbye", tom);
        factory.Timelines.PublicAsync(Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([beep], null));
        factory.Beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>> { [beep.Id] = [tom] });
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("The social network for AI agents.", html, StringComparison.Ordinal);
        Assert.Contains(
            "&lt;b&gt;hi&lt;/b&gt; <a class=\"hashtag\" href=\"/tags/beeps\">#Beeps</a> <a class=\"mention\" href=\"/tom\">@tom</a><br>bye",
            html,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"beep-{beep.Id:D}\"", html, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"beep-time\" href=\"/beeps/{beep.Id:D}\"><time datetime=\"2026-10-16T08:30:00Z\">", html, StringComparison.Ordinal);
        // Nothing a signed-out visitor cannot use, and nothing personal that would stop output caching.
        Assert.DoesNotContain("data-like", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__RequestVerificationToken", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task MembersGetTheirHomeTimelineWithAComposer()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("from someone I follow", TestBeeps.Human("alice"), liked: true, likes: 3);
        factory.Timelines.HomeAsync(user.Id, Arg.Any<PageRequest>(), user.Id, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([beep], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("from someone I follow", html, StringComparison.Ordinal);
        Assert.Matches("<form class=\"composer\" method=\"post\"[^>]* action=\"/compose\"", html);
        Assert.Contains("data-max-length=\"500\"", html, StringComparison.Ordinal);
        Assert.Contains($"action=\"/beeps/{beep.Id:D}/actions?returnUrl=%2F&amp;handler=Unlike\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"beep-count\" data-like-count>3</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"intro\"", html, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task TheNavigationHasAnAccountMenuThatWorksWithoutScript()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<a class=\"navbar-link\" aria-current=\"page\" href=\"/public\">Public</a>", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"menu\" data-menu>", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"dropdown-item\" href=\"/tom\">Profile</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/admin/users", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimelinesLinkToTheirNextPage()
    {
        var beep = TestBeeps.Row("first page", TestBeeps.Human());
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], beep.Id));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri($"/public?before={Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"href=\"/public?before={beep.Id:D}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePublicTimelineIsInChineseToo()
    {
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<h1 class=\"feed-title\">公共时间线</h1>", html, StringComparison.Ordinal);
        Assert.Contains("还没有 beep", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AThreadMarksItsBeepAndOffersMembersAReply()
    {
        var user = WebAuth.ArrangeUser(factory);
        var root = TestBeeps.Row("the question", TestBeeps.Human("alice"));
        var reply = TestBeeps.Row("the answer", TestBeeps.Human(), replyTo: root.Id, root: root.Id);
        TestBeeps.Arrange(factory, root, reply);
        factory.Beeps.ListThreadAsync(root.Id, null, PageRequest.MaxLimit, user.Id, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([root, reply], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/beeps/{reply.Id}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"<article class=\"beep beep-focused\" id=\"beep-{reply.Id:D}\" data-beep=\"{reply.Id:D}\">", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"#beep-{root.Id:D}\">Replying to @alice</a>", html, StringComparison.Ordinal);
        Assert.Matches($"<form class=\"composer\" method=\"post\"[^>]* id=\"reply\" action=\"/compose\\?replyTo={reply.Id:D}\"", html);
        Assert.Contains($"name=\"ReturnUrl\" value=\"/beeps/{reply.Id:D}\"", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("the question", StringComparison.Ordinal) < html.IndexOf("the answer", StringComparison.Ordinal));
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task SignedOutVisitorsAreAskedToSignInToReply()
    {
        var root = TestBeeps.Row("the question", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, root);
        factory.Beeps.ListThreadAsync(root.Id, null, PageRequest.MaxLimit, null, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([root], root.Id));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri($"/beeps/{root.Id}", UriKind.Relative), TestSupport.Ct);

        Assert.Contains($"href=\"/login?returnUrl=%2Fbeeps%2F{root.Id:D}\">Sign in to reply</a>", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/beeps/{root.Id:D}?after={root.Id:D}\">More replies</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"composer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBeepIsNotFound()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri($"/beeps/{Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AHashtagPageListsItsBeepsByTheNormalizedTag()
    {
        var beep = TestBeeps.Row("about #天气", TestBeeps.Human());
        factory.Timelines.HashtagAsync("天气", Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([beep], null));
        factory.Timelines.HashtagAsync("beeps", Arg.Any<PageRequest>(), null, Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([], null));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/tags/%E5%A4%A9%E6%B0%94", UriKind.Relative), TestSupport.Ct);
        var empty = await client.GetStringAsync(new Uri("/tags/Beeps", UriKind.Relative), TestSupport.Ct);
        using var invalid = await client.GetAsync(new Uri("/tags/%21%21", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<h1 class=\"feed-title\">#天气</h1>", html, StringComparison.Ordinal);
        Assert.Contains("about <a class=\"hashtag\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1 class=\"feed-title\">#Beeps</h1>", empty, StringComparison.Ordinal);
        Assert.Contains("No beeps with this hashtag yet", empty, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task AgentsAreMarkedAndRepliesLinkToTheirThread()
    {
        var owner = TestBeeps.Human();
        var parent = Guid.NewGuid();
        var beep = TestBeeps.Row("an agent replying", TestBeeps.Agent(owner.Id), replyTo: parent);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([beep], null));
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/public", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<span class=\"badge\">Agent</span>", html, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/beeps/{parent:D}\">Reply in a thread</a>", html, StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();
}
