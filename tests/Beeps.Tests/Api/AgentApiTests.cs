using System.Net;
using System.Text.Json;
using Beeps.Features.Agents;
using Beeps.Features.RateLimiting;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

/// <summary>Own factory per class instance: tests arrange keys on the shared store mocks.</summary>
public sealed class AgentApiTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task MeReturnsTheAgentAndItsOwner()
    {
        var agent = ArrangeAgent();
        using var client = agent.Client(factory);

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        var me = json.RootElement;
        Assert.Equal(agent.AgentId, me.GetProperty("id").GetGuid());
        Assert.Equal("agent", me.GetProperty("kind").GetString());
        Assert.Equal("tom.claude", me.GetProperty("handle").GetString());
        Assert.Equal("Claude Opus 4.5", me.GetProperty("model_name").GetString());
        Assert.True(me.GetProperty("connected").GetBoolean());
        Assert.Equal("2026-10-16T08:30:00Z", me.GetProperty("created_at").GetString());
        Assert.Equal($"https://beeps.test/avatars/{agent.AgentId:D}.svg", me.GetProperty("avatar_url").GetString());
        var owner = me.GetProperty("owner");
        Assert.Equal(agent.OwnerId, owner.GetProperty("id").GetGuid());
        Assert.Equal("owner", owner.GetProperty("relation").GetString());
        Assert.Equal("human", owner.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task MeIsLimitedPerAgent()
    {
        var first = ArrangeAgent();
        var second = ArrangeAgent(first.OwnerId);

        foreach (var agent in (TestAgent[])[first, first, second])
        {
            using var client = agent.Client(factory);
            using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // From the same address, for the same owner: only the agent tells the counters apart.
        var keys = factory.RateLimitStore.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IRateLimitStore.TryAcquireAsync)).Select(c => Assert.Single((IReadOnlyList<RateLimitCounter>)c.GetArguments()[0]!).Key).ToList();
        Assert.Equal(3, keys.Count);
        Assert.All(keys, k => Assert.StartsWith(RateLimitPolicies.AgentRead + ":", k, StringComparison.Ordinal));
        Assert.Equal(keys[0], keys[1]);
        Assert.NotEqual(keys[0], keys[2]);
    }

    [Theory]
    [InlineData(null, "Send your agent key")]
    [InlineData("beeps_notarealkey", "This agent key is invalid")]
    public async Task WithoutAValidKeyTheAnswerIs401(string? key, string detail)
    {
        using var client = factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", key);
        }

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        Assert.Equal("unauthorized", json.RootElement.GetProperty("code").GetString());
        Assert.StartsWith(detail, json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastTheLimitOnWrongKeysFromAnAddressNoKeyFromItIsLookedUp()
    {
        factory.RateLimitStore.WaitAsync(Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c[0].Key.StartsWith(RateLimitPolicies.AgentKey + ":", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromSeconds(30));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", AgentKeys.Generate());

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter?.Delta);
        await factory.Agents.DidNotReceiveWithAnyArgs().FindActiveKeyAsync(default!, TestSupport.Ct);
    }

    [Fact]
    public async Task AWrongKeyCountsAgainstItsAddress()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", AgentKeys.Generate());

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await factory.RateLimitStore.Received().TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c[0].Key.StartsWith(RateLimitPolicies.AgentKey + ":", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASessionCookieCannotCallTheAgentApi()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAgentKeyCannotOpenPages()
    {
        var agent = TestAgent.Arrange(factory);
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", agent.Key);

        using var response = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();

    private TestAgent ArrangeAgent(Guid? ownerId = null)
    {
        var agent = TestAgent.Arrange(factory, ownerId: ownerId);
        factory.Agents.GetAgentWithOwnerAsync(agent.AgentId, Arg.Any<CancellationToken>())
            .Returns(new AgentWithOwnerRow(agent.AgentId, "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, agent.OwnerId, "tom", "Tom"));
        return agent;
    }
}
