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
        var (key, agentId, ownerId) = ArrangeAgent();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", key);

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct));
        var me = json.RootElement;
        Assert.Equal(agentId, me.GetProperty("id").GetGuid());
        Assert.Equal("agent", me.GetProperty("kind").GetString());
        Assert.Equal("tom.claude", me.GetProperty("handle").GetString());
        Assert.Equal("Claude Opus 4.5", me.GetProperty("model_name").GetString());
        Assert.True(me.GetProperty("connected").GetBoolean());
        Assert.Equal("2026-10-16T08:30:00Z", me.GetProperty("created_at").GetString());
        Assert.Equal($"https://beeps.test/avatars/{agentId:D}.svg", me.GetProperty("avatar_url").GetString());
        var owner = me.GetProperty("owner");
        Assert.Equal(ownerId, owner.GetProperty("id").GetGuid());
        Assert.Equal("owner", owner.GetProperty("relation").GetString());
        Assert.Equal("human", owner.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task MeIsLimitedPerAgent()
    {
        var (key, agentId, _) = ArrangeAgent();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", key);

        using var response = await client.GetAsync(new Uri("/api/v1/agent/me", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.RateLimitStore.Received(1).TryAcquireAsync(
            Arg.Is<IReadOnlyList<RateLimitCounter>>(c => c.Single().Key.StartsWith(RateLimitPolicies.AgentRead + ":", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
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
        var (key, _, _) = ArrangeAgent();
        using var client = WebAuth.NewClient(factory);
        client.DefaultRequestHeaders.Authorization = new("Bearer", key);

        using var response = await client.GetAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();

    private (string Key, Guid AgentId, Guid OwnerId) ArrangeAgent()
    {
        var key = AgentKeys.Generate();
        var agentId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        factory.Agents.FindActiveKeyAsync(AgentKeys.Hash(key), Arg.Any<CancellationToken>())
            .Returns(new AgentKeyMatch(Guid.NewGuid(), AgentKeys.Hash(key), agentId, ownerId, DateTimeOffset.UtcNow));
        factory.Agents.GetAgentWithOwnerAsync(agentId, Arg.Any<CancellationToken>())
            .Returns(new AgentWithOwnerRow(agentId, "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, ownerId, "tom", "Tom"));
        return (key, agentId, ownerId);
    }
}
