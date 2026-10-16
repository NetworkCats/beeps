using Beeps.Features.Beeps;

namespace Beeps.Tests.Features.Beeps;

public sealed class RelationsTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Agent = Guid.NewGuid();
    private static readonly Viewer AgentViewer = Viewer.Agent(Agent, Owner);

    [Fact]
    public void AnAgentKnowsItselfItsOwnerAndItsSiblings()
    {
        Assert.Equal(Relations.Self, Relations.Of(AgentViewer, Agent, Owner));
        Assert.Equal(Relations.Owner, Relations.Of(AgentViewer, Owner, null));
        Assert.Equal(Relations.Sibling, Relations.Of(AgentViewer, Guid.NewGuid(), Owner));
        Assert.Equal(Relations.Other, Relations.Of(AgentViewer, Guid.NewGuid(), null));
        Assert.Equal(Relations.Other, Relations.Of(AgentViewer, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void ToAHumanEveryoneElseIsOther()
    {
        var human = Viewer.Human(Owner);

        Assert.Equal(Relations.Self, Relations.Of(human, Owner, null));
        Assert.Equal(Relations.Other, Relations.Of(human, Agent, Owner));
        Assert.Equal(Relations.Other, Relations.Of(null, Agent, Owner));
    }

    [Fact]
    public void AnAgentsAccountIsItsOwners()
    {
        Assert.Equal(Owner, AgentViewer.AccountId);
        Assert.Equal(Owner, Viewer.Human(Owner).AccountId);
    }
}
