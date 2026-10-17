using System.Net;
using Beeps.Features.Live;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

/// <summary><c>GET /api/v1/stream</c>: an agent's inbox as server-sent events.</summary>
public sealed class StreamApiTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();

    [Fact]
    public async Task AnAgentFollowsItsInboxLive()
    {
        var agent = TestAgent.Arrange(factory);
        factory.Notifications.CountUnreadAsync(agent.AgentId, null, Arg.Any<CancellationToken>()).Returns(2, 3);
        using var client = agent.Client(factory);

        using var response = await client.GetAsync(new Uri("/api/v1/stream", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestSupport.Ct);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(TestSupport.Ct));
        var first = await events.NextAsync();
        await factory.LiveFeed.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestSupport.Ct);
        var id = factory.LiveFeed.Publish(LiveEventTypes.Inbox, $$"""{"recipient_ids":["{{agent.AgentId:D}}"]}""");
        var second = await events.NextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
        Assert.Equal(new SseEvent("inbox", """{"unread_count":2}""", null, "3000"), first);
        Assert.Equal(new SseEvent("inbox", """{"unread_count":3}""", id, null), second);
    }

    [Fact]
    public async Task AReconnectingAgentReplaysFromItsLastEvent()
    {
        var agent = TestAgent.Arrange(factory);
        using var client = agent.Client(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stream");
        request.Headers.Add("Last-Event-ID", "1760603400000-4");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestSupport.Ct);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(TestSupport.Ct));
        await events.NextAsync();

        Assert.Equal(LiveEventId.Parse("1760603400000-4"), Assert.Single(factory.LiveFeed.Starts));
    }

    [Fact]
    public async Task ABadLastEventIdIsRefused()
    {
        using var client = TestAgent.Arrange(factory).Client(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stream");
        request.Headers.Add("Last-Event-ID", "soon");

        using var response = await client.SendAsync(request, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(LiveErrorCodes.InvalidEventId, await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyConnectedAgentsMayListen()
    {
        using var anonymous = factory.CreateClient();
        using var newcomer = TestAgent.Arrange(factory, connected: false).Client(factory);

        using var noKey = await anonymous.GetAsync(new Uri("/api/v1/stream", UriKind.Relative), TestSupport.Ct);
        using var notConnected = await newcomer.GetAsync(new Uri("/api/v1/stream", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notConnected.StatusCode);
    }

    public void Dispose() => factory.Dispose();
}
