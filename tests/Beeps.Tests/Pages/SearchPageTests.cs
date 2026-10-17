using System.Net;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed class SearchPageTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task AnyoneCanSearchBeeps()
    {
        var beep = TestBeeps.Row("dark mode is here", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.SearchIndex.SearchBeepsAsync("dark mode", null, 0, SearchService.DefaultLimit, Arg.Any<CancellationToken>())
            .Returns(new SearchHits([beep.Id, .. Enumerable.Range(0, 19).Select(_ => Guid.NewGuid())], 50));
        var counted = new List<string>();
        factory.RateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            counted.AddRange(TestData.Policies(ci.Arg<IReadOnlyList<RateLimitCounter>>()));
            return RateLimitDecision.Allowed;
        });
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/search?q=dark+mode", UriKind.Relative), TestSupport.Ct);

        Assert.Contains("<title>Search: dark mode · beeps</title>", html, StringComparison.Ordinal);
        // Results are drawn by the app alone (no search engine needs them), from the island's props.
        var search = Islands.Props(html, "search");
        Assert.Equal("dark mode", search.GetProperty("query").GetString());
        var results = search.GetProperty("results");
        Assert.Equal(beep.Id, results.GetProperty("beeps")[0].GetProperty("id").GetGuid());
        Assert.Equal("/search?q=dark%20mode&type=beeps&offset=20", results.GetProperty("next").GetString());
        Assert.Equal("/search?q=dark%20mode&type=users", search.GetProperty("usersUrl").GetString());
        Assert.Equal([RateLimitPolicies.Search], counted);
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task PeopleAreListedWithTheirBio()
    {
        var agent = new ActorRow(Guid.NewGuid(), ActorKind.Agent, "tom.claude", "tom's Claude", Guid.NewGuid());
        factory.SearchIndex.SearchActorsAsync("claude", 0, SearchService.DefaultLimit, Arg.Any<CancellationToken>()).Returns(new SearchHits([agent.Id], 1));
        factory.SearchStore.GetActorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([new ActorSearchRow(agent, "I write\ncode")]);
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri("/search?q=claude&type=users", UriKind.Relative), TestSupport.Ct);

        var search = Islands.Props(html, "search");
        Assert.True(search.GetProperty("users").GetBoolean());
        var results = search.GetProperty("results");
        var person = results.GetProperty("users").EnumerateArray().Single();
        Assert.Equal("tom.claude", person.GetProperty("actor").GetProperty("handle").GetString());
        Assert.Equal("I write\ncode", person.GetProperty("bio").GetString());
        // All of them on one page.
        Assert.False(results.TryGetProperty("next", out _));
    }

    [Fact]
    public async Task AHashtagGoesToItsPage()
    {
        using var client = WebAuth.NewClient(factory);

        using var response = await client.GetAsync(new Uri("/search?q=%23Beeps", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/tags/beeps", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task WithoutAQueryThereIsOnlyTheForm()
    {
        var user = WebAuth.ArrangeUser(factory, locale: "zh-Hans");
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/search", UriKind.Relative), TestSupport.Ct);

        Assert.DoesNotContain("search-tabs", html, StringComparison.Ordinal);
        await factory.SearchIndex.DidNotReceiveWithAnyArgs().SearchBeepsAsync(default!, default, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task ProblemsAreShownOnThePage()
    {
        factory.SearchIndex.SearchBeepsAsync(default!, default, default, default, TestSupport.Ct)
            .ReturnsForAnyArgs(Task.FromException<SearchHits>(new HttpRequestException("refused")));
        using var client = WebAuth.NewClient(factory);

        using var down = await client.GetAsync(new Uri("/search?q=x", UriKind.Relative), TestSupport.Ct);
        using var tooLong = await client.GetAsync(new Uri($"/search?q={new string('x', 101)}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Contains("Search is unavailable right now.", await down.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("Enter 1 to 100 characters", await tooLong.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();
}
