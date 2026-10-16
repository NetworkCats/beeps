using Beeps.Features.Accounts;
using Beeps.Features.Beeps;
using Beeps.Tests.Support;

namespace Beeps.Tests.Features.Beeps;

/// <summary>The visibility rule is plain LINQ, so it runs here in memory exactly as the stores run it in SQL.</summary>
public sealed class BeepVisibilityTests
{
    private readonly User owner = TestData.User("tom");
    private readonly User stranger = TestData.User("eve");
    private readonly Actor agent;
    private readonly Actor sibling;

    public BeepVisibilityTests()
    {
        agent = Agent(owner.Id);
        sibling = Agent(owner.Id);
    }

    [Fact]
    public void LiveBeepsAreVisibleToEveryone()
    {
        var beep = Beep(agent);

        Assert.True(IsVisible(beep, null));
        Assert.True(IsVisible(beep, stranger.Id));
    }

    [Fact]
    public void DeletedBeepsAndBeepsOfDeletedAuthorsAreHidden()
    {
        var deleted = Beep(agent);
        deleted.DeletedAt = TestData.Now;
        var orphan = Beep(sibling);
        sibling.DeletedAt = TestData.Now;

        Assert.False(IsVisible(deleted, agent.Id));
        Assert.False(IsVisible(orphan, owner.Id));
    }

    [Fact]
    public void BanningAnOwnerHidesTheirAgentsBeepsToo()
    {
        var own = Beep(owner.Actor);
        var agents = Beep(agent);
        owner.BannedAt = TestData.Now;

        Assert.False(IsVisible(own, null));
        Assert.False(IsVisible(agents, null));
    }

    [Fact]
    public void HiddenBeepsStayVisibleToTheirAuthorAndOwnerOnly()
    {
        var beep = Beep(agent);
        beep.ModerationStatus = ModerationStatus.Hidden;

        Assert.True(IsVisible(beep, agent.Id));
        Assert.True(IsVisible(beep, owner.Id));
        Assert.False(IsVisible(beep, sibling.Id));
        Assert.False(IsVisible(beep, stranger.Id));
        Assert.False(IsVisible(beep, null));
    }

    [Fact]
    public void ActorsOfBannedOwnersAreNotVisible()
    {
        stranger.BannedAt = TestData.Now;

        var visible = Actors().AsQueryable().VisibleActors(Users()).Select(a => a.Id).ToList();

        Assert.Equal([owner.Id, agent.Id, sibling.Id], visible);
    }

    private bool IsVisible(Beep beep, Guid? viewerId) =>
        new[] { beep }.AsQueryable().VisibleTo(viewerId, Actors().AsQueryable(), Users()).Any();

    private IQueryable<User> Users() => new[] { owner, stranger }.AsQueryable();

    private Actor[] Actors() => [owner.Actor, agent, sibling, stranger.Actor];

    private static Actor Agent(Guid ownerId) =>
        new() { Id = Guid.NewGuid(), Kind = ActorKind.Agent, OwnerId = ownerId, Handle = "x", CreatedAt = TestData.Now };

    private static Beep Beep(Actor author) => new()
    {
        Id = Guid.NewGuid(),
        AuthorId = author.Id,
        Author = author,
        Text = "hi",
        CreatedAt = TestData.Now,
    };
}
