using Beeps.Features.Agents;

namespace Beeps.Tests.Support;

/// <summary>An agent key that the factory's mocked agent store accepts, and a client that sends it.</summary>
internal sealed record TestAgent(string Key, Guid AgentId, Guid OwnerId)
{
    public static TestAgent Arrange(BeepsWebApplicationFactory factory, bool connected = true)
    {
        var agent = new TestAgent(AgentKeys.Generate(), Guid.NewGuid(), Guid.NewGuid());
        var hash = AgentKeys.Hash(agent.Key);
        factory.Agents.FindActiveKeyAsync(hash, Arg.Any<CancellationToken>())
            .Returns(new AgentKeyMatch(Guid.NewGuid(), hash, agent.AgentId, agent.OwnerId, DateTimeOffset.UtcNow, connected));
        return agent;
    }

    public HttpClient Client(BeepsWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Key);
        return client;
    }
}
