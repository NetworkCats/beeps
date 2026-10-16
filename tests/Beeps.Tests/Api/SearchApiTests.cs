using System.Net;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.RateLimiting;
using Beeps.Features.Search;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

/// <summary><c>GET /api/v1/search</c> through the real pipeline, with the engine mocked.</summary>
public sealed class SearchApiTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();
    private readonly TestAgent agent;
    private readonly HttpClient client;

    public SearchApiTests()
    {
        agent = TestAgent.Arrange(factory);
        client = agent.Client(factory);
    }

    [Fact]
    public async Task BeepsAreFoundBestFirstWithAWayToTheNextPage()
    {
        var beep = TestBeeps.Row("dark mode #beeps", TestBeeps.Human("alice"));
        TestBeeps.Arrange(factory, beep);
        factory.SearchIndex.SearchBeepsAsync("dark", "beeps", 0, 1, Arg.Any<CancellationToken>()).Returns(new SearchHits([beep.Id], 40));
        var counted = new List<string>();
        factory.RateLimitStore.TryAcquireAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(ci =>
        {
            counted.AddRange(TestData.Policies(ci.Arg<IReadOnlyList<RateLimitCounter>>()));
            return RateLimitDecision.Allowed;
        });

        using var response = await client.GetAsync(new Uri("/api/v1/search?q=dark&hashtag=Beeps&limit=1", UriKind.Relative), TestSupport.Ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = Assert.Single(json.RootElement.GetProperty("beeps").EnumerateArray());
        Assert.Equal(beep.Id, found.GetProperty("id").GetGuid());
        Assert.Equal("other", found.GetProperty("author").GetProperty("relation").GetString());
        Assert.Empty(json.RootElement.GetProperty("users").EnumerateArray());
        Assert.Equal(1, json.RootElement.GetProperty("next_offset").GetInt32());
        Assert.Equal([RateLimitPolicies.Search], counted);
    }

    [Fact]
    public async Task PeopleAreFoundWithTheirBio()
    {
        var owner = new ActorRow(agent.OwnerId, ActorKind.Human, "tom", "Tom", null);
        factory.SearchIndex.SearchActorsAsync("tom", 0, 20, Arg.Any<CancellationToken>()).Returns(new SearchHits([owner.Id], 1));
        factory.SearchStore.GetActorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([new ActorSearchRow(owner, "hi")]);

        using var response = await client.GetAsync(new Uri("/api/v1/search?q=tom&type=users", UriKind.Relative), TestSupport.Ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));

        var user = Assert.Single(json.RootElement.GetProperty("users").EnumerateArray());
        Assert.Equal(("tom", "hi", "owner", "human"), (user.GetProperty("handle").GetString(), user.GetProperty("bio").GetString(), user.GetProperty("relation").GetString(), user.GetProperty("kind").GetString()));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("next_offset").ValueKind);
    }

    [Theory]
    [InlineData("/api/v1/search?q=", HttpStatusCode.BadRequest, SearchErrorCodes.QueryInvalid)]
    [InlineData("/api/v1/search?q=a&type=posts", HttpStatusCode.BadRequest, SearchErrorCodes.TypeInvalid)]
    public async Task BadSearchesSayWhy(string url, HttpStatusCode status, string code)
    {
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnreachableEngineIs503()
    {
        factory.SearchIndex.SearchBeepsAsync(default!, default, default, default, TestSupport.Ct)
            .ReturnsForAnyArgs(Task.FromException<SearchHits>(new HttpRequestException("refused")));

        using var response = await client.GetAsync(new Uri("/api/v1/search?q=a", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(SearchErrorCodes.Unavailable, await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
