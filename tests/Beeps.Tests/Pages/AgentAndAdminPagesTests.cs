using System.Net;
using System.Text.RegularExpressions;
using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Live;
using Beeps.Features.Shared;
using Beeps.Tests.Support;

namespace Beeps.Tests.Pages;

public sealed partial class AgentAndAdminPagesTests : IDisposable
{
    // Fixed, but at the real time: the sign-in cookie expires by the test client's own clock.
    private readonly FixedTimeProvider time = new(DateTimeOffset.UtcNow);
    private readonly BeepsWebApplicationFactory factory;

    public AgentAndAdminPagesTests() => factory = new() { Time = time };

    [Fact]
    public async Task OnboardingShowsTheNewKeyInTheHandOffSentence()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/onboarding/agent", "/onboarding/agent");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = Islands.Props(html, "handoff").GetProperty("created");
        var prompt = created.GetProperty("prompt").GetString()!;
        var key = KeyPattern().Match(prompt).Value;
        Assert.True(AgentKeys.IsWellFormed(key));
        Assert.Contains($"Read https://beeps.test/skill.md and follow it to use beeps. Your personal key is: {key}.", prompt, StringComparison.Ordinal);
        Assert.StartsWith(created.GetProperty("keyPrefix").GetString()!, key, StringComparison.Ordinal);
        PageAssert.NoInlineScriptsOrStyles(html);
        factory.Agents.Received(1).AddKey(Arg.Is<AgentKey>(k => k.KeyHash == AgentKeys.Hash(key)));
    }

    [Fact]
    public async Task TheAgentsPageListsAgentsAndConfirmsDestructiveActions()
    {
        var user = WebAuth.ArrangeUser(factory);
        factory.Agents.ListAgentsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(
            [new AgentRow(Guid.NewGuid(), "tom.claude", "tom's Claude", "Claude Opus 4.5", TestData.Now, null, "beeps_abc123", TestData.Now)]);
        using var client = await WebAuth.SignInAsync(factory, user);

        var html = await client.GetStringAsync(new Uri("/agents", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/agents");

        var agent = Islands.Props(page, "agents").GetProperty("agents").EnumerateArray().Single();
        Assert.Equal("tom's Claude", agent.GetProperty("displayName").GetString());
        Assert.Equal("beeps_abc123", agent.GetProperty("keyPrefix").GetString());
        Assert.Equal(TestData.Now, agent.GetProperty("keyLastUsedAt").GetDateTimeOffset());
        PageAssert.NoInlineScriptsOrStyles(html);
    }

    [Fact]
    public async Task DisablingRedirectsWithANotice()
    {
        var user = WebAuth.ArrangeUser(factory);
        var agent = new Actor { Id = Guid.NewGuid(), Kind = ActorKind.Agent, OwnerId = user.Id };
        factory.Agents.FindAgentAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await WebAuth.PostFormAsync(client, "/agents", $"/agents?handler=Disable&id={agent.Id}");
        var page = await Islands.PageAsync(client, "/agents");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(agent.DisabledAt);
        Assert.Contains("Agent disabled.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAdminAreaIsForAdminsOnly()
    {
        var user = WebAuth.ArrangeUser(factory);
        using var client = await WebAuth.SignInAsync(factory, user);

        using var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Not allowed", await response.Content.ReadAsStringAsync(TestSupport.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminsSeeTheUserList()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        factory.Admin.ListUsersAsync(null, null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminUserRow>(
            [new AdminUserRow(admin.Id, "tom", "tom", "tom@example.com", true, true, TestData.Now, null, null)],
            Guid.NewGuid()));
        using var client = await WebAuth.SignInAsync(factory, admin);

        var html = await client.GetStringAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);
        var page = await Islands.PageAsync(client, "/admin/users");

        var users = Islands.Props(page, "admin-users").GetProperty("users");
        Assert.Equal("tom@example.com", users.GetProperty("items").EnumerateArray().Single().GetProperty("email").GetString());
        Assert.Contains("before=", users.GetProperty("next").GetString(), StringComparison.Ordinal);
        // The account menu opens the admin area for an admin.
        Assert.True(Islands.Config(page).GetProperty("viewer").GetProperty("admin").GetBoolean());
        PageAssert.NoInlineScriptsOrStyles(html);
        // The page brings the admin area's navigation along, and the wide layout and its stylesheet.
        var data = Islands.Data(page);
        var main = data.GetProperty("main").GetString();
        Assert.Contains("<nav class=\"nav nav-horizontal admin-nav\"", main, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", main, StringComparison.Ordinal);
        Assert.Contains("<div class=\"island\" data-island=\"admin-users\"></div>", main, StringComparison.Ordinal);
        Assert.True(data.GetProperty("shell").GetProperty("wide").GetBoolean());
        Assert.Equal(["main", "member", "admin"], Islands.Strings(data.GetProperty("shell").GetProperty("styles")));
    }

    [Fact]
    public async Task AdminRightsAreRecheckedOnEveryRequest()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        using var client = await WebAuth.SignInAsync(factory, admin);

        admin.IsAdmin = false;
        using var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative), TestSupport.Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminSettingsShowANewInviteOnce()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        factory.Admin.ListInvitesAsync(default, TestSupport.Ct).ReturnsForAnyArgs([]);
        using var client = await WebAuth.SignInAsync(factory, admin);

        using var response = await WebAuth.PostFormAsync(client, "/admin/settings", "/admin/settings?handler=CreateInvite");
        var html = await response.Content.ReadAsStringAsync(TestSupport.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Open", Islands.Props(html, "admin-settings").GetProperty("mode").GetString());
        factory.Admin.Received(1).AddAuditLog(Arg.Is<AdminAuditLog>(l => l.Action == AuditActions.CreateInvite && l.AdminId == admin.Id));
        // Only in this answer: the page's island keeps it while the invite is still open. Other admins' pages hear of it.
        var invite = Islands.Props(html, "admin-settings").GetProperty("newInvite");
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", invite.GetProperty("code").GetString());
        Assert.Single(TestData.LiveEvents(factory.LiveEvents, LiveEventTypes.AdminChanged));
    }

    [Fact]
    public async Task BanningFromTheDetailsPageReportsTheOutcome()
    {
        var admin = WebAuth.ArrangeUser(factory, admin: true);
        var target = WebAuth.ArrangeUser(factory, handle: "spammer");
        factory.Admin.FindUserAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        factory.Admin.GetUserAsync(target.Id, Arg.Any<CancellationToken>()).Returns(
            new AdminUserRow(target.Id, "spammer", "spammer", "spammer@example.com", false, true, TestData.Now, null, null));
        factory.Agents.DisableAllAsync(default, default, TestSupport.Ct).ReturnsForAnyArgs([]);
        using var client = await WebAuth.SignInAsync(factory, admin);

        using var response = await WebAuth.PostFormAsync(client, $"/admin/users/{target.Id}", $"/admin/users/{target.Id}?handler=Ban");
        var page = await Islands.PageAsync(client, $"/admin/users/{target.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(time.Now, target.BannedAt);
        Assert.Contains("User banned.", page, StringComparison.Ordinal);
    }

    public void Dispose() => factory.Dispose();

    [GeneratedRegex("beeps_[0-9A-Za-z]{43}")]
    private static partial Regex KeyPattern();
}
