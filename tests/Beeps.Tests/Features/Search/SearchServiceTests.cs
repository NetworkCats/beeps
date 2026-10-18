using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beeps.Tests.Features.Search;

public sealed class SearchServiceTests
{
    private static readonly Viewer Tom = Viewer.Human(Guid.NewGuid());

    private readonly ISearchIndex index = Substitute.For<ISearchIndex>();
    private readonly ISearchStore store = Substitute.For<ISearchStore>();
    private readonly IBeepStore beeps = Substitute.For<IBeepStore>();
    private readonly IBeepCounterStore counters = Substitute.For<IBeepCounterStore>();

    public SearchServiceTests()
    {
        beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>>());
        beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>());
        counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
    }

    [Theory]
    [InlineData("#Beeps", "beeps")]
    [InlineData("  #天气 ", "天气")]
    [InlineData("beeps", null)]
    [InlineData("#", null)]
    [InlineData("#a b", null)]
    [InlineData(null, null)]
    public void AHashtagInTheSearchBoxOpensItsPage(string? query, string? tag) => Assert.Equal(tag, SearchService.HashtagShortcut(query));

    [Fact]
    public async Task BeepsComeInTheIndexsOrderAndOnlyIfTheViewerMayStillSeeThem()
    {
        var best = TestBeeps.Row("dark mode", TestBeeps.Human("alice"));
        var second = TestBeeps.Row("dark", TestBeeps.Human("bob"));
        var deletedSince = Guid.NewGuid();
        index.SearchBeepsAsync("dark", null, 0, SearchService.DefaultLimit, Arg.Any<CancellationToken>())
            .Returns(new SearchHits([best.Id, deletedSince, second.Id], 3));
        beeps.GetRowsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Tom.ActorId, Arg.Any<CancellationToken>()).Returns([second, best]);

        var result = await Create().SearchAsync(Tom, new SearchRequest(" dark "), TestSupport.Ct);

        Assert.Equal([best.Id, second.Id], result.Value.Beeps.Select(b => b.Id));
        Assert.Empty(result.Value.Users);
        Assert.Null(result.Value.NextOffset);
    }

    [Fact]
    public async Task AHashtagFilterIsNormalizedAndAllowsAnEmptyQuery()
    {
        index.SearchBeepsAsync("", "beeps", 0, 5, Arg.Any<CancellationToken>()).Returns(new SearchHits([], 0));

        var result = await Create().SearchAsync(null, new SearchRequest(null, SearchTypes.Beeps, "#Beeps", Limit: 5), TestSupport.Ct);

        Assert.True(result.IsSuccess);
        await index.Received(1).SearchBeepsAsync("", "beeps", 0, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeopleComeWithTheirBioAndRelation()
    {
        var agent = new ActorRow(Guid.NewGuid(), ActorKind.Agent, "tom.claude", "tom's Claude", Tom.ActorId);
        index.SearchActorsAsync("claude", 20, 20, Arg.Any<CancellationToken>()).Returns(new SearchHits([agent.Id, Guid.NewGuid()], 100));
        store.GetActorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([new ActorSearchRow(agent, "writes code")]);

        var result = await Create().SearchAsync(Tom, new SearchRequest("claude", SearchTypes.Users, Offset: 20), TestSupport.Ct);

        var found = Assert.Single(result.Value.Users);
        Assert.Equal(("tom.claude", "writes code", Relations.Other), (found.Actor.Handle, found.Bio, found.Actor.Relation));
        // Two hits on a page of twenty: the index has no more.
        Assert.Null(result.Value.NextOffset);
    }

    [Fact]
    public async Task PagingStopsAtTheLastReachableResult()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();
        index.SearchActorsAsync(default!, default, default, TestSupport.Ct).ReturnsForAnyArgs(new SearchHits(ids, 5000));
        store.GetActorsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs([]);
        var service = Create();

        var first = await service.SearchUsersAsync(null, "a", 0, null, TestSupport.Ct);
        var last = await service.SearchUsersAsync(null, "a", 980, null, TestSupport.Ct);
        var near = await service.SearchUsersAsync(null, "a", 990, null, TestSupport.Ct);
        var beyond = await service.SearchUsersAsync(null, "a", 1000, null, TestSupport.Ct);

        Assert.Equal(20, first.Value.NextOffset);
        Assert.Null(last.Value.NextOffset);
        await index.Received(1).SearchActorsAsync("a", 990, 10, Arg.Any<CancellationToken>());
        Assert.Null(near.Value.NextOffset);
        Assert.Equal(SearchErrorCodes.QueryInvalid, beyond.Error?.Code);
    }

    [Theory]
    [InlineData(SearchTypes.Beeps, "")]
    [InlineData(SearchTypes.Users, " ")]
    [InlineData(SearchTypes.Users, "a\u0001b")]
    public async Task QueriesMustSaySomething(string type, string query)
    {
        var result = await Create().SearchAsync(Tom, new SearchRequest(query, type), TestSupport.Ct);

        Assert.Equal(SearchErrorCodes.QueryInvalid, result.Error?.Code);
        await index.DidNotReceiveWithAnyArgs().SearchActorsAsync(default!, default, default, TestSupport.Ct);
    }

    [Fact]
    public async Task QueriesAreAtMostAHundredCharactersAsPeopleCountThem()
    {
        index.SearchBeepsAsync(default!, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(new SearchHits([], 0));
        beeps.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs([]);
        var service = Create();

        var hundred = await service.SearchAsync(Tom, new SearchRequest(string.Concat(Enumerable.Repeat("😀", 100))), TestSupport.Ct);
        var more = await service.SearchAsync(Tom, new SearchRequest(new string('a', 101)), TestSupport.Ct);

        Assert.True(hundred.IsSuccess);
        Assert.Equal(SearchErrorCodes.QueryInvalid, more.Error?.Code);
    }

    [Fact]
    public async Task UnknownTypesAndHashtagsAreRejected()
    {
        var service = Create();

        Assert.Equal(SearchErrorCodes.TypeInvalid, (await service.SearchAsync(Tom, new SearchRequest("a", "posts"), TestSupport.Ct)).Error?.Code);
        Assert.Equal(BeepErrorCodes.InvalidHashtag, (await service.SearchAsync(Tom, new SearchRequest("a", Hashtag: "a b"), TestSupport.Ct)).Error?.Code);
    }

    [Fact]
    public async Task AnUnreachableEngineIsReportedAsUnavailable()
    {
        index.SearchBeepsAsync(default!, default, default, default, TestSupport.Ct).ReturnsForAnyArgs(Task.FromException<SearchHits>(new HttpRequestException("refused")));

        var result = await Create().SearchAsync(Tom, new SearchRequest("a"), TestSupport.Ct);

        Assert.Equal((ErrorKind.Unavailable, SearchErrorCodes.Unavailable), (result.Error?.Kind, result.Error?.Code));
    }

    [Fact]
    public async Task AMentionBeingTypedIsOfferedPeopleByTheStartOfTheirHandle()
    {
        var agent = new ActorRow(Guid.NewGuid(), ActorKind.Agent, "Tom.Claude", "tom's Claude", Tom.ActorId);
        store.SuggestActorsAsync("tom.c", Tom, SearchService.MaxSuggestions, Arg.Any<CancellationToken>()).Returns([agent]);

        var people = await Create().SuggestMentionsAsync(Tom, "Tom.C", TestSupport.Ct);

        var found = Assert.Single(people);
        Assert.Equal(("Tom.Claude", ActorKind.Agent), (found.Handle, found.Kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".tom")]
    [InlineData("tom.a.b")]
    [InlineData("tom%")]
    [InlineData("天气")]
    public async Task TextNoHandleStartsWithIsOfferedNobody(string? prefix)
    {
        var people = await Create().SuggestMentionsAsync(Tom, prefix, TestSupport.Ct);

        Assert.Empty(people);
        await store.DidNotReceiveWithAnyArgs().SuggestActorsAsync(default!, default!, default, TestSupport.Ct);
    }

    private SearchService Create() =>
        new(index, store, beeps, new BeepViews(beeps, counters, TestData.Avatars, TestData.Media, NullLogger<BeepViews>.Instance), new TestLocalizer(), NullLogger<SearchService>.Instance);
}
