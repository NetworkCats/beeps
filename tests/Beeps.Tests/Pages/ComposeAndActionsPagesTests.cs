using System.Net;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class ComposeAndActionsPagesTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task PostingGoesBackToTheNewBeepWithANotice()
    {
        var user = WebAuth.ArrangeUser(factory);
        Beep? posted = null;
        factory.Beeps.When(b => b.AddBeep(Arg.Any<Beep>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>()))
            .Do(ci => posted = ci.Arg<Beep>());
        factory.Beeps.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(ci =>
            posted is null ? [] : (IReadOnlyList<BeepRow>)[new BeepRow(posted.Id, posted.Text, TestBeeps.Human(id: user.Id), null, posted.Id, TestData.Now, 0, 0, false)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", "/compose", ("text", "  hello beeps  "), ("ReturnUrl", "/"));
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("hello beeps", posted?.Text);
        Assert.Equal($"/#beep-{posted!.Id:D}", response.Headers.Location?.OriginalString);
        Assert.Contains("Beep posted.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedBeepComesBackWithItsText()
    {
        var user = WebAuth.ArrangeUser(factory);
        var text = new string('字', BeepText.MaxLength + 1);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", "/compose", ("text", text), ("ReturnUrl", "/"));
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // The composer keeps the text, its field marked with why it was refused.
        var compose = Islands.Envelope(html, "compose");
        Assert.Equal(text, compose.GetProperty("props").GetProperty("composer").GetProperty("text").GetString());
        Assert.True(compose.GetProperty("errors").TryGetProperty("Text", out _));
        factory.Beeps.DidNotReceiveWithAnyArgs().AddBeep(default!, default!, default!);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task ARateLimitedPostSaysWhenToTryAgain()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);
        factory.RateLimitStore.TryAcquireAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.Any(x => x.Key.StartsWith(RateLimitPolicies.HumanPost, StringComparison.Ordinal))), Arg.Any<CancellationToken>())
            .Returns(RateLimitDecision.Denied(TimeSpan.FromMinutes(20)));

        using var form = new FormUrlEncodedContent(
            [KeyValuePair.Create("text", "one too many"), KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html))]);
        using var response = await client.PostAsync(new Uri("/compose", UriKind.Relative), form, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(20), response.Headers.RetryAfter?.Delta);
        var refused = Islands.Props(await response.Content.ReadAsStringAsync(TestSupport.Ct), "compose");
        Assert.Equal("one too many", refused.GetProperty("composer").GetProperty("text").GetString());
    }

    [Fact]
    public async Task TheReplyPageShowsWhatIsBeingAnswered()
    {
        var user = WebAuth.ArrangeUser(factory);
        var parent = TestBeeps.Row("answer me", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, parent);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri($"/compose?replyTo={parent.Id}", UriKind.Relative), TestSupport.Ct);
        using var missing = await client.GetAsync(new Uri($"/compose?replyTo={Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);

        var compose = Islands.Props(html, "compose");
        Assert.Equal("answer me", compose.GetProperty("parent").GetProperty("html").GetString());
        // A reply's composer (its placeholder says so).
        Assert.Equal(parent.Id, compose.GetProperty("composer").GetProperty("replyTo").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SignedOutVisitorsAreSentToSignInBeforePosting()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/compose", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login?ReturnUrl=%2Fcompose", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLikeScriptGetsJustAStatus()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("like me", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.Beeps.AddLikeAsync(user.Id, beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        using var client = await WebAuth.SignInAsync(factory, user);
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        using var form = new FormUrlEncodedContent([KeyValuePair.Create("__RequestVerificationToken", TestSupport.AntiforgeryToken(html))]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/beeps/{beep.Id}/actions?handler=Like&returnUrl=%2F", UriKind.Relative)) { Content = form };
        request.Headers.Add("X-Requested-With", "fetch");
        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await factory.Beeps.Received(1).AddLikeAsync(user.Id, beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutScriptALikeGoesBackToTheBeep()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("like me", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", $"/beeps/{beep.Id}/actions?handler=Unlike&returnUrl=%2Falice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/alice#beep-{beep.Id:D}", response.Headers.Location?.OriginalString);
        await factory.Beeps.Received(1).RemoveLikeAsync(user.Id, beep.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyLocalPlacesAreGoneBackTo()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("like me", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", $"/beeps/{beep.Id}/actions?handler=Like&returnUrl=https%3A%2F%2Fevil.example%2F");

        Assert.Equal($"/beeps/{beep.Id:D}#beep-{beep.Id:D}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task DeletingABeepFromItsOwnPageGoesHome()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("oops", TestBeeps.Human(id: user.Id));
        TestBeeps.Arrange(factory, beep);
        factory.Beeps.DeleteAsync(beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", $"/beeps/{beep.Id}/actions?handler=Delete&returnUrl=%2Fbeeps%2F{beep.Id}");
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("Beep deleted.", html, StringComparison.Ordinal);
        await factory.Beeps.Received(1).DeleteAsync(beep.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SomeoneElsesBeepIsNotDeletedAndTheReasonIsShown()
    {
        var user = WebAuth.ArrangeUser(factory);
        var beep = TestBeeps.Row("not yours", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/", $"/beeps/{beep.Id}/actions?handler=Delete&returnUrl=%2F");
        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("You can only delete your own beeps.", html, StringComparison.Ordinal);
        await factory.Beeps.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task OwnBeepsAndOwnAgentsBeepsHaveADeleteButton()
    {
        var user = WebAuth.ArrangeUser(factory);
        var mine = TestBeeps.Row("mine", TestBeeps.Human(id: user.Id));
        var myAgents = TestBeeps.Row("my agent's", TestBeeps.Agent(user.Id));
        var theirs = TestBeeps.Row("theirs", TestBeeps.Human("alice"));
        factory.Timelines.HomeAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([mine, myAgents, theirs], null));
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestSupport.Ct);

        var items = Islands.Props(html, "index").GetProperty("list").GetProperty("items").EnumerateArray().ToDictionary(b => b.GetProperty("id").GetGuid());
        Assert.True(items[mine.Id].GetProperty("canDelete").GetBoolean());
        Assert.True(items[myAgents.Id].GetProperty("canDelete").GetBoolean());
        Assert.False(items[theirs.Id].TryGetProperty("canDelete", out _));
    }

    public void Dispose() => factory.Dispose();
}
