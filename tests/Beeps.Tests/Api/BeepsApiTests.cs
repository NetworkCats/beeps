using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Beeps.Features.Accounts;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Tests.Support;

namespace Beeps.Tests.Api;

/// <summary>Every agent endpoint of stage 2 through the real pipeline: auth, rate limits, binding and JSON.</summary>
public sealed class BeepsApiTests : IDisposable
{
    private readonly BeepsWebApplicationFactory factory = new();
    private readonly TestAgent agent;
    private readonly HttpClient client;

    public BeepsApiTests()
    {
        agent = TestAgent.Arrange(factory);
        client = agent.Client(factory);
    }

    [Fact]
    public async Task HelloConnectsTheAgent()
    {
        var newcomer = TestAgent.Arrange(factory, connected: false);
        var before = new AgentWithOwnerRow(newcomer.AgentId, null, null, null, TestData.Now, newcomer.OwnerId, "tom", "Tom");
        var after = before with { Handle = "tom.claude", DisplayName = "tom's Claude", ModelName = "Claude Opus 4.5" };
        factory.Agents.GetAgentWithOwnerAsync(newcomer.AgentId, Arg.Any<CancellationToken>()).Returns(before, after);
        factory.Agents.ListHandlesStartingWithAsync("tom.claude", Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        factory.Agents.TryConnectAsync(newcomer.AgentId, "Claude Opus 4.5", "tom's Claude", "tom.claude", Arg.Any<CancellationToken>()).Returns(true);
        using var newcomerClient = newcomer.Client(factory);

        using var response = await newcomerClient.PostAsJsonAsync("/api/v1/agent/hello", new { model_name = "Claude Opus 4.5" }, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await JsonAsync(response);
        Assert.Equal("tom.claude", me.GetProperty("handle").GetString());
        Assert.True(me.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task AnAgentThatHasNotSaidHelloCanOnlySayHello()
    {
        using var newcomer = TestAgent.Arrange(factory, connected: false).Client(factory);

        using var response = await newcomer.GetAsync(new Uri("/api/v1/timelines/public", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("agent_not_connected", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostingReturnsTheNewBeep()
    {
        Beep? added = null;
        factory.Beeps.When(s => s.AddBeep(Arg.Any<Beep>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>()))
            .Do(ci => added = ci.Arg<Beep>());
        factory.Beeps.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(_ => [Row(added!.Id, agent.AgentId, added.Text)]);

        using var response = await client.PostAsJsonAsync("/api/v1/beeps", new { text = "hello #beeps" }, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/beeps/{added!.Id:D}", response.Headers.Location?.ToString());
        var beep = await JsonAsync(response);
        Assert.Equal("hello #beeps", beep.GetProperty("text").GetString());
        Assert.Equal("self", beep.GetProperty("author").GetProperty("relation").GetString());
        Assert.Equal("beeps", beep.GetProperty("hashtags")[0].GetString());
        Assert.Equal(JsonValueKind.Null, beep.GetProperty("reply_to_id").ValueKind);
    }

    [Fact]
    public async Task BadTextIsAValidationProblem()
    {
        using var response = await client.PostAsJsonAsync("/api/v1/beeps", new { text = new string('a', 501) }, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal("text_too_long", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("text", out _));
    }

    [Fact]
    public async Task GettingABeep()
    {
        var id = Guid.CreateVersion7();
        factory.Beeps.GetRowsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)), agent.AgentId, Arg.Any<CancellationToken>())
            .Returns([Row(id, agent.OwnerId, "hi")]);

        using var found = await client.GetAsync(new Uri($"/api/v1/beeps/{id}", UriKind.Relative), TestSupport.Ct);
        using var missing = await client.GetAsync(new Uri($"/api/v1/beeps/{Guid.NewGuid()}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal("owner", (await JsonAsync(found)).GetProperty("author").GetProperty("relation").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GettingAThread()
    {
        var root = Guid.CreateVersion7();
        var after = Guid.CreateVersion7();
        factory.Beeps.FindVisibleAsync(root, agent.AgentId, Arg.Any<CancellationToken>()).Returns(new BeepRef(root, agent.OwnerId, null, root, null, 0, 0));
        factory.Beeps.ListThreadAsync(root, after, 5, agent.AgentId, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<BeepRow>([Row(root, agent.OwnerId, "root")], root));

        using var response = await client.GetAsync(new Uri($"/api/v1/beeps/{root}/thread?after={after}&limit=5", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await JsonAsync(response);
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(root, page.GetProperty("next_cursor").GetGuid());
    }

    [Fact]
    public async Task DeletingLikingAndUnliking()
    {
        var id = Guid.CreateVersion7();
        factory.Beeps.FindVisibleAsync(id, agent.AgentId, Arg.Any<CancellationToken>()).Returns(new BeepRef(id, agent.AgentId, agent.OwnerId, id, null, 0, 0));

        using var like = await client.PostAsync(new Uri($"/api/v1/beeps/{id}/like", UriKind.Relative), null, TestSupport.Ct);
        using var unlike = await client.DeleteAsync(new Uri($"/api/v1/beeps/{id}/like", UriKind.Relative), TestSupport.Ct);
        using var delete = await client.DeleteAsync(new Uri($"/api/v1/beeps/{id}", UriKind.Relative), TestSupport.Ct);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent], [like.StatusCode, unlike.StatusCode, delete.StatusCode]);
        await factory.Beeps.Received(1).AddLikeAsync(agent.AgentId, id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await factory.Beeps.Received(1).RemoveLikeAsync(agent.AgentId, id, Arg.Any<CancellationToken>());
        await factory.Beeps.Received(1).DeleteAsync(id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheTimelinesBindTheirPaging()
    {
        var before = Guid.CreateVersion7();
        var since = Guid.CreateVersion7();
        var page = new PageRequest(before, since, 5);
        var empty = new CursorPage<BeepRow>([], null);
        factory.Timelines.PublicAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs(empty);
        factory.Timelines.HomeAsync(default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(empty);
        factory.Timelines.HashtagAsync(default!, default!, default, TestSupport.Ct).ReturnsForAnyArgs(empty);
        var query = $"before={before}&since_id={since}&limit=5";

        using var publicTimeline = await client.GetAsync(new Uri($"/api/v1/timelines/public?{query}", UriKind.Relative), TestSupport.Ct);
        using var owner = await client.GetAsync(new Uri($"/api/v1/timelines/owner?{query}", UriKind.Relative), TestSupport.Ct);
        using var tag = await client.GetAsync(new Uri($"/api/v1/tags/%E5%A4%A9%E6%B0%94/beeps?{query}", UriKind.Relative), TestSupport.Ct);

        Assert.All([publicTimeline, owner, tag], r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await factory.Timelines.Received(1).PublicAsync(page, agent.AgentId, Arg.Any<CancellationToken>());
        await factory.Timelines.Received(1).HomeAsync(agent.OwnerId, page, agent.AgentId, Arg.Any<CancellationToken>());
        await factory.Timelines.Received(1).HashtagAsync("天气", page, agent.AgentId, Arg.Any<CancellationToken>());
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(publicTimeline)).GetProperty("next_cursor").ValueKind);
    }

    [Fact]
    public async Task ProfilesAndTheirBeeps()
    {
        var tom = new ActorRow(agent.OwnerId, ActorKind.Human, "tom", "Tom", null);
        var self = new ActorRow(agent.AgentId, ActorKind.Agent, "tom.claude", "tom's Claude", agent.OwnerId);
        factory.Profiles.FindByHandleAsync("tom", Arg.Any<CancellationToken>()).Returns(new ProfileRow(tom, "bio", null, TestData.Now, null));
        factory.Profiles.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns([self]);
        factory.Timelines.ProfileAsync(default, default, default!, default, TestSupport.Ct).ReturnsForAnyArgs(new CursorPage<BeepRow>([], null));

        using var profile = await client.GetAsync(new Uri("/api/v1/users/tom", UriKind.Relative), TestSupport.Ct);
        using var beeps = await client.GetAsync(new Uri("/api/v1/users/tom/beeps", UriKind.Relative), TestSupport.Ct);
        using var missing = await client.GetAsync(new Uri("/api/v1/users/nobody", UriKind.Relative), TestSupport.Ct);

        var json = await JsonAsync(profile);
        Assert.Equal("owner", json.GetProperty("relation").GetString());
        Assert.Equal("self", json.GetProperty("agents")[0].GetProperty("relation").GetString());
        Assert.Equal(HttpStatusCode.OK, beeps.StatusCode);
        // The API lists beeps and replies together; only the web page splits them into tabs.
        await factory.Timelines.Received(1).ProfileAsync(tom.Id, ProfileTimeline.All, Arg.Any<PageRequest>(), agent.AgentId, Arg.Any<CancellationToken>());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ReadingTheInbox()
    {
        var beepId = Guid.CreateVersion7();
        var from = new ActorRow(agent.OwnerId, ActorKind.Human, "tom", "Tom", null);
        factory.Notifications.ListAsync(agent.AgentId, PageRequest.Create(), true, Arg.Any<CancellationToken>())
            .Returns(new CursorPage<NotificationRow>([new NotificationRow(Guid.CreateVersion7(), NotificationType.Mention, from, beepId, TestData.Now, null)], null));
        factory.Beeps.GetRowsAsync(default!, default, TestSupport.Ct).ReturnsForAnyArgs([Row(beepId, agent.OwnerId, "@tom.claude please look")]);

        using var response = await client.GetAsync(new Uri("/api/v1/notifications?unread_only=true", UriKind.Relative), TestSupport.Ct);

        var item = (await JsonAsync(response)).GetProperty("items")[0];
        Assert.Equal("mention", item.GetProperty("type").GetString());
        Assert.Equal("owner", item.GetProperty("from").GetProperty("relation").GetString());
        Assert.False(item.GetProperty("read").GetBoolean());
    }

    [Fact]
    public async Task MarkingTheInboxRead()
    {
        var upTo = Guid.CreateVersion7();

        using var some = await client.PostAsJsonAsync("/api/v1/notifications/read", new { up_to_id = upTo }, TestSupport.Ct);
        using var all = await client.PostAsync(new Uri("/api/v1/notifications/read", UriKind.Relative), null, TestSupport.Ct);

        Assert.Equal(HttpStatusCode.NoContent, some.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        await factory.Notifications.Received(1).MarkReadAsync(agent.AgentId, upTo, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await factory.Notifications.Received(1).MarkReadAsync(agent.AgentId, null, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    internal static BeepRow Row(Guid id, Guid authorId, string text) =>
        new(id, text, new ActorRow(authorId, ActorKind.Human, "tom", "Tom", null), null, id, TestData.Now, 0, 0, false);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestSupport.Ct)).RootElement.Clone();
}
